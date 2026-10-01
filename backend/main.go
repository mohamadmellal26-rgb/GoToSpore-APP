package main

import (
	"database/sql"
	"fmt"
	"log"
	"net/http"
	"sync"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/golang-jwt/jwt/v5"
	"github.com/gorilla/websocket"
	_ "github.com/mattn/go-sqlite3"
	"golang.org/x/crypto/bcrypt"
)

var jwtSecret = []byte("super_secret_key_12345")
var db *sql.DB

var upgrader = websocket.Upgrader{
	ReadBufferSize:  1024,
	WriteBufferSize: 1024,
	CheckOrigin: func(r *http.Request) bool {
		return true
	},
}

// Structs الخاصة بالمستخدمين
type User struct {
	ID                  string  `json:"id"`
	Username            string  `json:"username" binding:"required"`
	Password            string  `json:"password" binding:"required"`
	FullName            string  `json:"full_name"`
	ImageURL            string  `json:"image_url"`
	TotalDistanceMeters float64 `json:"total_distance_meters"`
	TotalDistanceKm     float64 `json:"total_distance_km"`
	MaxSpeedKmh         float64 `json:"max_speed_kmh"`
	TotalActivities     int     `json:"total_activities"`
	TotalCalories       float64 `json:"total_calories"`
}

type LoginInput struct {
	Username string `json:"username" binding:"required"`
	Password string `json:"password" binding:"required"`
}

type UserStatsInput struct {
	DistanceMeters float64 `json:"distance_meters"`
	Calories       float64 `json:"calories"`
	SpeedKmh       float64 `json:"speed_kmh"`
}

type UserProfileResponse struct {
	Username            string  `json:"username"`
	FullName            string  `json:"full_name"`
	ImageURL            string  `json:"image_url"`
	TotalDistanceMeters float64 `json:"total_distance_meters"`
	TotalDistanceKm     float64 `json:"total_distance_km"`
	MaxSpeedKmh         float64 `json:"max_speed_kmh"`
	TotalActivities     int     `json:"total_activities"`
	TotalCalories       float64 `json:"total_calories"`
}

type Claims struct {
	Username string `json:"username"`
	jwt.RegisteredClaims
}

type Message struct {
	Type     string  `json:"type"`
	Username string  `json:"username"`
	Content  string  `json:"content"`
	Color    string  `json:"color"`
	Calories float64 `json:"calories"`
	Duration int     `json:"duration"`
	Distance float64 `json:"distance"`
}

// WebSockets structs
type Client struct {
	Hub  *LiveHub
	Conn *websocket.Conn
	Send chan Message
}

type LiveHub struct {
	Clients    map[*Client]bool
	Broadcast  chan Message
	Register   chan *Client
	Unregister chan *Client
	Mutex      sync.RWMutex
}

func newLiveHub() *LiveHub {
	return &LiveHub{
		Broadcast:  make(chan Message),
		Register:   make(chan *Client),
		Unregister: make(chan *Client),
		Clients:    make(map[*Client]bool),
	}
}

func (h *LiveHub) Run() {
	for {
		select {
		case client := <-h.Register:
			h.Mutex.Lock()
			h.Clients[client] = true
			h.Mutex.Unlock()

		case client := <-h.Unregister:
			h.Mutex.Lock()
			if _, ok := h.Clients[client]; ok {
				delete(h.Clients, client)
				close(client.Send)
			}
			h.Mutex.Unlock()

		case message := <-h.Broadcast:
			h.Mutex.RLock()
			for client := range h.Clients {
				select {
				case client.Send <- message:
				default:
					go func(c *Client) {
						h.Unregister <- c
					}(client)
				}
			}
			h.Mutex.RUnlock()
		}
	}
}

func (c *Client) ReadPump() {
	defer func() {
		c.Hub.Unregister <- c
		c.Conn.Close()
	}()

	for {
		var msg Message
		err := c.Conn.ReadJSON(&msg)
		if err != nil {
			break
		}
		c.Hub.Broadcast <- msg
	}
}

func (c *Client) WritePump() {
	defer func() {
		c.Conn.Close()
	}()

	for {
		select {
		case message, ok := <-c.Send:
			if !ok {
				c.Conn.WriteMessage(websocket.CloseMessage, []byte{})
				return
			}
			if err := c.Conn.WriteJSON(message); err != nil {
				return
			}
		}
	}
}

// تهيئة قاعدة البيانات SQLite
func initDB() {
	var err error
	db, err = sql.Open("sqlite3", "./gotospore.db")
	if err != nil {
		log.Fatalf("فشل في فتح قاعدة البيانات: %v", err)
	}

	createTableQuery := `
	CREATE TABLE IF NOT EXISTS users (
		username TEXT PRIMARY KEY,
		password TEXT NOT NULL,
		full_name TEXT,
		image_url TEXT,
		total_distance_meters REAL DEFAULT 0,
		max_speed_kmh REAL DEFAULT 0,
		total_activities INTEGER DEFAULT 0,
		total_calories REAL DEFAULT 0
	);`

	_, err = db.Exec(createTableQuery)
	if err != nil {
		log.Fatalf("فشل في إنشاء جدول المستخدمين: %v", err)
	}
}

// Helpers للتشفير والتوكن
func hashPassword(password string) (string, error) {
	bytes, err := bcrypt.GenerateFromPassword([]byte(password), bcrypt.DefaultCost)
	return string(bytes), err
}

func checkPasswordHash(password, hash string) bool {
	err := bcrypt.CompareHashAndPassword([]byte(hash), []byte(password))
	return err == nil
}

func generateToken(username string) (string, error) {
	expirationTime := time.Now().Add(24 * time.Hour)
	claims := &Claims{
		Username: username,
		RegisteredClaims: jwt.RegisteredClaims{
			ExpiresAt: jwt.NewNumericDate(expirationTime),
			IssuedAt:  jwt.NewNumericDate(time.Now()),
		},
	}

	token := jwt.NewWithClaims(jwt.SigningMethodHS256, claims)
	return token.SignedString(jwtSecret)
}

// Middleware للمصادقة
func AuthMiddleware() gin.HandlerFunc {
	return func(c *gin.Context) {
		tokenString := c.GetHeader("Authorization")

		if tokenString == "" || len(tokenString) < 7 || tokenString[:7] != "Bearer " {
			c.JSON(http.StatusUnauthorized, gin.H{"error": "غير مصرح"})
			c.Abort()
			return
		}

		tokenString = tokenString[7:]

		claims := &Claims{}
		token, err := jwt.ParseWithClaims(tokenString, claims, func(token *jwt.Token) (interface{}, error) {
			if _, ok := token.Method.(*jwt.SigningMethodHMAC); !ok {
				return nil, fmt.Errorf("طريقة التوقيع غير متوافقة")
			}
			return jwtSecret, nil
		})

		if err != nil || !token.Valid {
			c.JSON(http.StatusUnauthorized, gin.H{"error": "توكن غير صالح"})
			c.Abort()
			return
		}

		c.Set("username", claims.Username)
		c.Next()
	}
}

func main() {
	initDB()
	defer db.Close()

	router := gin.Default()

	liveHub := newLiveHub()
	go liveHub.Run()

	// 1. تسجيل حساب جديد
	router.POST("/api/register", func(c *gin.Context) {
		var newUser User
		if err := c.ShouldBindJSON(&newUser); err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": "البيانات المدخلة غير صالحة"})
			return
		}

		var existingUser string
		err := db.QueryRow("SELECT username FROM users WHERE username = ?", newUser.Username).Scan(&existingUser)
		if err == nil {
			c.JSON(http.StatusConflict, gin.H{"error": "اسم المستخدم مستخدم بالفعل"})
			return
		}

		hashedPassword, err := hashPassword(newUser.Password)
		if err != nil {
			c.JSON(http.StatusInternalServerError, gin.H{"error": "فشل في تشفير كلمة المرور"})
			return
		}

		_, err = db.Exec(`
			INSERT INTO users (username, password, full_name, image_url, total_distance_meters, max_speed_kmh, total_activities, total_calories) 
			VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
			newUser.Username, hashedPassword, newUser.FullName, newUser.ImageURL, newUser.TotalDistanceMeters, newUser.MaxSpeedKmh, newUser.TotalActivities, newUser.TotalCalories)

		if err != nil {
			c.JSON(http.StatusInternalServerError, gin.H{"error": "فشل حفظ الحساب في قاعدة البيانات"})
			return
		}

		c.JSON(http.StatusCreated, gin.H{"message": "تم إنشاء الحساب بنجاح"})
	})

	// 2. تسجيل الدخول
	router.POST("/api/login", func(c *gin.Context) {
		var input LoginInput
		if err := c.ShouldBindJSON(&input); err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": "البيانات المدخلة غير صالحة"})
			return
		}

		var user User
		err := db.QueryRow("SELECT username, password, full_name, image_url FROM users WHERE username = ?", input.Username).
			Scan(&user.Username, &user.Password, &user.FullName, &user.ImageURL)

		if err != nil || !checkPasswordHash(input.Password, user.Password) {
			c.JSON(http.StatusUnauthorized, gin.H{"error": "اسم المستخدم أو كلمة المرور غير صحيحة"})
			return
		}

		token, err := generateToken(user.Username)
		if err != nil {
			c.JSON(http.StatusInternalServerError, gin.H{"error": "فشل في إنشاء التوكن"})
			return
		}

		c.JSON(http.StatusOK, gin.H{
			"message":   "تم تسجيل الدخول بنجاح",
			"token":     token,
			"username":  user.Username,
			"full_name": user.FullName,
			"user": gin.H{
				"username":  user.Username,
				"image_url": user.ImageURL,
			},
		})
	})

	// 3. WebSocket للبث المباشر
	router.GET("/ws/live", func(c *gin.Context) {
		conn, err := upgrader.Upgrade(c.Writer, c.Request, nil)
		if err != nil {
			return
		}

		client := &Client{
			Hub:  liveHub,
			Conn: conn,
			Send: make(chan Message, 256),
		}

		client.Hub.Register <- client

		go client.WritePump()
		go client.ReadPump()
	})

	// 4. المسارات المحمية
	protected := router.Group("/api")
	protected.Use(AuthMiddleware())
	{
		// الحصول على معلومات الملف الشخصي
		protected.GET("/user/profile", func(c *gin.Context) {
			usernameVal, _ := c.Get("username")
			username := usernameVal.(string)

			var user User
			err := db.QueryRow(`
				SELECT username, full_name, image_url, total_distance_meters, max_speed_kmh, total_activities, total_calories 
				FROM users WHERE username = ?`, username).
				Scan(&user.Username, &user.FullName, &user.ImageURL, &user.TotalDistanceMeters, &user.MaxSpeedKmh, &user.TotalActivities, &user.TotalCalories)

			if err != nil {
				c.JSON(http.StatusNotFound, gin.H{"error": "المستخدم غير موجود"})
				return
			}

			c.JSON(http.StatusOK, UserProfileResponse{
				Username:            user.Username,
				FullName:            user.FullName,
				ImageURL:            user.ImageURL,
				TotalDistanceMeters: user.TotalDistanceMeters,
				TotalDistanceKm:     user.TotalDistanceMeters / 1000.0,
				MaxSpeedKmh:         user.MaxSpeedKmh,
				TotalActivities:     user.TotalActivities,
				TotalCalories:       user.TotalCalories,
			})
		})

		// حفظ الكيلومترات والمسافة المحفوظة محلياً عند المزامنة أو Logout
		protected.POST("/user/stats", func(c *gin.Context) {
			usernameVal, _ := c.Get("username")
			username := usernameVal.(string)

			var stats UserStatsInput
			if err := c.ShouldBindJSON(&stats); err != nil {
				c.JSON(http.StatusBadRequest, gin.H{"error": "البيانات غير صالحة"})
				return
			}

			// تحديث المسافة بحفظ القيمة الأعلى دائماً لتفادي ضياع الكيلومترات
			_, err := db.Exec(`
				UPDATE users 
				SET total_distance_meters = MAX(total_distance_meters, ?),
				    total_calories = MAX(total_calories, ?),
				    max_speed_kmh = MAX(max_speed_kmh, ?)
				WHERE username = ?`,
				stats.DistanceMeters, stats.Calories, stats.SpeedKmh, username)

			if err != nil {
				c.JSON(http.StatusInternalServerError, gin.H{"error": "فشل تحديث البيانات"})
				return
			}

			c.JSON(http.StatusOK, gin.H{"message": "تم حفظ الكيلومترات بنجاح"})
		})
	}

	log.Println("Server running on port 8080")
	if err := router.Run(":8080"); err != nil {
		log.Fatalf("Server failed to start: %v", err)
	}
}