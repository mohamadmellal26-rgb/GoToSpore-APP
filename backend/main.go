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

var jwtSecret = []byte("super_secret_key_12345")

var upgrader = websocket.Upgrader{
    ReadBufferSize:  1024,
    WriteBufferSize: 1024,
    CheckOrigin: func(r *http.Request) bool {
        return true
    },
}

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

var (
    usersDb = make(map[string]User)
    dbMutex sync.RWMutex
)

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

        case client := <-h.Unregister:
            h.Mutex.Lock()
            if _, ok := h.Clients[client]; ok {
                delete(h.Clients, client)
                close(client.Send)
            }
            h.Mutex.Unlock()

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

        newUser.Password = hashedPassword
        newUser.TotalDistanceKm = newUser.TotalDistanceMeters / 1000.0
        usersDb[newUser.Username] = newUser

        c.JSON(http.StatusCreated, gin.H{"message": "تم إنشاء الحساب بنجاح"})
    })

    // 2. تسجيل الدخول
    router.POST("/api/login", func(c *gin.Context) {
        var inputUser User
        if err := c.ShouldBindJSON(&inputUser); err != nil {
            c.JSON(http.StatusBadRequest, gin.H{"error": "البيانات المدخلة غير صالحة"})
            return
        }

        dbMutex.RLock()
        user, exists := usersDb[inputUser.Username]
        dbMutex.RUnlock()

        if !exists || !checkPasswordHash(inputUser.Password, user.Password) {
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
                "id":        user.ID,
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
        protected.GET("/user/profile", func(c *gin.Context) {
            username, _ := c.Get("username")

            dbMutex.RLock()
            user, exists := usersDb[username.(string)]
            dbMutex.RUnlock()

            if !exists {
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
    }

    log.Println("Server running on port 8080")
    router.Run(":8080")
}