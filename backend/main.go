package main

import (
	"fmt"
	"log"
	"net/http"
	"sync"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/golang-jwt/jwt/v5"
	"github.com/gorilla/websocket"
	"golang.org/x/crypto/bcrypt"
)

// المفتاح السري لتوقيع الـ JWT
var jwtSecret = []byte("super_secret_key_12345")

// إعدادات WebSocket Upgrader
var upgrader = websocket.Upgrader{
	ReadBufferSize:  1024,
	WriteBufferSize: 1024,
	CheckOrigin: func(r *http.Request) bool {
		return true // السماح بجميع الاتصالات (في الإنتاج يفضل تقييد Origin)
	},
}

// هيكل نموذج بيانات المستخدم
type User struct {
	ID       string `json:"id"`
	Username string `json:"username" binding:"required"`
	Password string `json:"password" binding:"required"`
}

// قاعدة بيانات مؤقتة في الذاكرة (In-Memory Database)
var (
	usersDb = make(map[string]string) // Key: Username, Value: Hashed Password
	dbMutex sync.RWMutex
)

// هيكل Claims للـ JWT
type Claims struct {
	Username string `json:"username"`
	jwt.RegisteredClaims
}

// ------------------------------------------------------------
// نظام إدارة البث المباشر والـ WebSockets (Live Hub)
// ------------------------------------------------------------

type Message struct {
	Type     string `json:"type"`     // "chat", "heart", "stats", "video_frame"
	Username string `json:"username"` // اسم الموفد
	Content  string `json:"content"`  // نص الرسالة أو البيانات
	Color    string `json:"color"`    // لون مستخدم في الشات
	Calories int    `json:"calories"` // لبيانات اللياقة
	Duration int    `json:"duration"` // مدة التمرين بالثواني
}

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
	Mutex      sync.Mutex
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
			log.Println("[LiveHub]: مشترك جديد انضم للبث")

		case client := <-h.Unregister:
			h.Mutex.Lock()
			if _, ok := h.Clients[client]; ok {
				delete(h.Clients, client)
				close(client.Send)
			}
			h.Mutex.Unlock()
			log.Println("[LiveHub]: مشترك غادر البث")

		case message := <-h.Broadcast:
			h.Mutex.Lock()
			for client := range h.Clients {
				select {
				case client.Send <- message:
				default:
					close(client.Send)
					delete(h.Clients, client)
				}
			}
			h.Mutex.Unlock()
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
			if websocket.IsUnexpectedCloseError(err, websocket.CloseGoingAway, websocket.CloseAbnormalClosure) {
				log.Printf("[WS Error]: %v", err)
			}
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
			c.Conn.WriteJSON(message)
		}
	}
}

// ------------------------------------------------------------
// الدوال المساعدة (Auth & Security)
// ------------------------------------------------------------

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

func AuthMiddleware() gin.HandlerFunc {
	return func(c *gin.Context) {
		tokenString := c.GetHeader("Authorization")

		if tokenString == "" || len(tokenString) < 7 || tokenString[:7] != "Bearer " {
			c.JSON(http.StatusUnauthorized, gin.H{"error": "غير مصرح: يلزم توفير توكن صالح"})
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
			c.JSON(http.StatusUnauthorized, gin.H{"error": "توكن غير صالح أو منتهي الصلاحية"})
			c.Abort()
			return
		}

		c.Set("username", claims.Username)
		c.Next()
	}
}

// ------------------------------------------------------------
// Main
// ------------------------------------------------------------

func main() {
	router := gin.Default()

	// إعداد وتحديد مجرى Live Hub للبث المباشر
	liveHub := newLiveHub()
	go liveHub.Run()

	// 1. مسار تسجيل حساب جديد (Register)
	router.POST("/api/register", func(c *gin.Context) {
		var newUser User
		if err := c.ShouldBindJSON(&newUser); err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": "البيانات المدخلة غير صالحة"})
			return
		}

		dbMutex.Lock()
		defer dbMutex.Unlock()

		if _, exists := usersDb[newUser.Username]; exists {
			c.JSON(http.StatusConflict, gin.H{"error": "اسم المستخدم مستخدم بالفعل"})
			return
		}

		hashedPassword, err := hashPassword(newUser.Password)
		if err != nil {
			c.JSON(http.StatusInternalServerError, gin.H{"error": "فشل في تشفير كلمة المرور"})
			return
		}

		usersDb[newUser.Username] = hashedPassword
		c.JSON(http.StatusCreated, gin.H{"message": "تم إنشاء الحساب بنجاح"})
	})

	// 2. مسار تسجيل الدخول (Login)
	router.POST("/api/login", func(c *gin.Context) {
		var inputUser User
		if err := c.ShouldBindJSON(&inputUser); err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": "البيانات المدخلة غير صالحة"})
			return
		}

		dbMutex.RLock()
		storedHash, exists := usersDb[inputUser.Username]
		dbMutex.RUnlock()

		if !exists || !checkPasswordHash(inputUser.Password, storedHash) {
			c.JSON(http.StatusUnauthorized, gin.H{"error": "اسم المستخدم أو كلمة المرور غير صحيحة"})
			return
		}

		token, err := generateToken(inputUser.Username)
		if err != nil {
			c.JSON(http.StatusInternalServerError, gin.H{"error": "فشل في إنشاء التوكن"})
			return
		}

		c.JSON(http.StatusOK, gin.H{
			"message": "تم تسجيل الدخول بنجاح",
			"token":   token,
		})
	})

	// 3. مسار WebSocket للبث المباشر (Live Stream Hub)
	router.GET("/ws/live", func(c *gin.Context) {
		conn, err := upgrader.Upgrade(c.Writer, c.Request, nil)
		if err != nil {
			log.Printf("[Upgrade Error]: %v", err)
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

	// 4. مسار محمي يتطلب AuthMiddleware (Protected Profile)
	protected := router.Group("/api")
	protected.Use(AuthMiddleware())
	{
		protected.GET("/profile", func(c *gin.Context) {
    username, _ := c.Get("username")
    c.JSON(http.StatusOK, gin.H{ // ✅ StatusOK صحيحة (OK بالحروف الكبيرة)
        "message":  "مرحباً بك في المنطقة المحمية",
        "username": username,
    })
})
	}

	log.Println("Server running on http://localhost:8080")
	router.Run(":8080")
}