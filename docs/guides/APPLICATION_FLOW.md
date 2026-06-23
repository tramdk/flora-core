# 🚀 Luồng Hoạt Động Ứng Dụng FloraCore

> Tài liệu chi tiết hướng dẫn luồng hoạt động của ứng dụng từ khi **khởi động** cho đến khi **xử lý xong một HTTP Request**.

---

## 📑 Mục Lục

1. [Tổng Quan Kiến Trúc](#1-tổng-quan-kiến-trúc)
2. [Luồng Khởi Động Ứng Dụng (Application Startup)](#2-luồng-khởi-động-ứng-dụng)
3. [Luồng Xử Lý Request (Request Pipeline)](#3-luồng-xử-lý-request)
4. [Luồng MediatR Pipeline (CQRS)](#4-luồng-mediatr-pipeline-cqrs)
5. [Ví Dụ Cụ Thể](#5-ví-dụ-cụ-thể)
6. [Sơ Đồ Tổng Hợp](#6-sơ-đồ-tổng-hợp)

---

## 1. Tổng Quan Kiến Trúc

Ứng dụng FloraCore được xây dựng theo **Clean Architecture** với 4 tầng chính:

```
┌──────────────────────────────────────────────────────┐
│                   Presentation Layer                 │
│         (Controllers, Middleware, Program.cs)        │
├──────────────────────────────────────────────────────┤
│                   Application Layer                  │
│    (Features/CQRS, Behaviors, Interfaces, DTOs)      │
├──────────────────────────────────────────────────────┤
│                   Infrastructure Layer               │
│  (EF Core, Repositories, Services, Hubs, Seeder)     │
├──────────────────────────────────────────────────────┤
│                     Domain Layer                     │
│           (Entities, Value Objects, Exceptions)       │
└──────────────────────────────────────────────────────┘
```

**Các thư viện chính:**

| Thư viện | Vai trò |
|---|---|
| **MediatR** | CQRS pattern - tách Command/Query khỏi Controller |
| **FluentValidation** | Validation tự động cho Request |
| **AutoMapper** | Ánh xạ Entity ↔ DTO |
| **Serilog** | Structured logging |
| **Entity Framework Core** | ORM (hỗ trợ SQL Server & PostgreSQL) |
| **ASP.NET Identity** | Authentication & User management |
| **SignalR** | Real-time communication (Chat, Notifications) |
| **AspNetCoreRateLimit** | Giới hạn tần suất request |
| **Scalar** | API Documentation UI |

---

## 2. Luồng Khởi Động Ứng Dụng

> **File:** `Program.cs`

Khi chạy `dotnet run`, ứng dụng thực hiện tuần tự các bước sau:

### 📊 Sơ đồ khởi động

```mermaid
flowchart TD
    A["dotnet run"] --> B["WebApplication.CreateBuilder()"]
    B --> C["Cấu hình JWT Claim Mapping"]
    C --> D["Cấu hình Serilog Logging"]
    D --> E["Đăng ký Services vào DI Container"]
    E --> F["builder.Build() → tạo WebApplication"]
    F --> G["Cấu hình Middleware Pipeline"]
    G --> H["Map Endpoints & SignalR Hubs"]
    H --> I["Khởi tạo Database Schema"]
    I --> J{"Môi trường Development?"}
    J -- Có --> K["Seed dữ liệu mẫu"]
    J -- Không --> L{"SEED_DATA == true?"}
    L -- Có --> K
    L -- Không --> M["app.Run() → Lắng nghe Request"]
    K --> M
```

### Bước 2.1 — Khởi tạo Host & Cấu hình cơ bản

```csharp
var builder = WebApplication.CreateBuilder(args);

// Xóa ánh xạ claim mặc định để giữ nguyên tên claim trong JWT
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
```

**Giải thích:** Mặc định, ASP.NET sẽ đổi tên các claim trong JWT token (ví dụ: `sub` → `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`). Dòng `.Clear()` ngăn điều này, giúp ta truy cập claim bằng tên gốc như `sub`, `email`, v.v.

### Bước 2.2 — Cấu hình Serilog

```csharp
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)  // Đọc config từ appsettings.json
    .Enrich.FromLogContext()                         // Thêm context vào log
    .CreateLogger();
builder.Host.UseSerilog();                           // Thay thế logger mặc định
```

**Cấu hình Serilog** (trong `appsettings.json`):
- Ghi log ra **Console** (stdout)
- Ghi log ra **File** (`Logs/log-{Date}.txt`, xoay vòng theo ngày)
- Level mặc định: `Information`, riêng `Microsoft.*` và `System.*`: `Warning`

### Bước 2.3 — Đăng ký Services (Dependency Injection)

Đây là bước quan trọng nhất, đăng ký tất cả service vào DI container:

```csharp
builder.Services
    .AddDatabaseServices(builder.Configuration)   // ① Database + Identity
    .AddDistributedMemoryCache()                  // ② In-memory cache
    .AddRateLimiting(builder.Configuration)       // ③ Rate Limiting
    .AddJwtAuthentication(builder.Configuration)  // ④ JWT Auth
    .AddSignalRServices()                         // ⑤ SignalR
    .AddApplicationServices()                     // ⑥ MediatR + Validators
    .AddInfrastructureServices()                  // ⑦ Repositories + Services
    .AddCorsPolicy();                             // ⑧ CORS
```

Chi tiết từng nhóm:

#### ① `AddDatabaseServices` — Database & Identity

```
📦 Đăng ký:
├── AppDbContext (EF Core) → SqlServer hoặc PostgreSQL
│   └── Tự động phát hiện & chuyển đổi URI format cho Postgres
├── ASP.NET Identity
│   ├── UserManager<AppUser>
│   ├── RoleManager<IdentityRole<Guid>>
│   └── SignInManager<AppUser>
└── Default Token Providers
```

**Lưu ý:** Hỗ trợ chuyển đổi tự động từ URI format (`postgres://user:pass@host/db`) sang Npgsql connection string — rất hữu ích khi deploy trên Render, Koyeb.

#### ② `AddDistributedMemoryCache` — Bộ nhớ đệm

Đăng ký `IDistributedCache` (in-memory) dùng cho **CachingBehavior** trong MediatR pipeline.

#### ③ `AddRateLimiting` — Giới hạn tần suất

```
📦 Đăng ký:
├── IMemoryCache
├── IpRateLimitOptions (từ appsettings.json)
│   ├── Tất cả endpoint: 100 request/phút
│   └── POST /api/auth/login: 5 request/phút
├── InMemoryRateLimiting store
└── RateLimitConfiguration
```

#### ④ `AddJwtAuthentication` — Xác thực JWT

```
📦 Đăng ký:
├── Authentication scheme: JwtBearer (mặc định)
├── TokenValidationParameters
│   ├── ValidateIssuer: true
│   ├── ValidateAudience: true
│   ├── ValidateLifetime: true
│   └── IssuerSigningKey: từ Jwt:Secret
└── JwtBearerEvents
    └── OnMessageReceived: Hỗ trợ token qua query string cho SignalR
```

#### ⑤ `AddSignalRServices` — Real-time

```
📦 Đăng ký:
├── SignalR core services
└── IUserIdProvider → UserIdProvider (custom, map userId từ JWT claim)
```

#### ⑥ `AddApplicationServices` — Application Layer

```
📦 Đăng ký:
├── IHttpContextAccessor
├── ICurrentUserService → CurrentUserService (Scoped)
├── FluentValidation validators (auto-scan assembly)
├── MediatR
│   ├── Handlers (auto-scan từ assembly)
│   └── Pipeline Behaviors (theo thứ tự):
│       ├── 1. LoggingBehavior       → Ghi log trước/sau
│       ├── 2. ValidationBehavior    → Validate FluentValidation
│       ├── 3. AuthorizationBehavior → Kiểm tra quyền ownership
│       └── 4. CachingBehavior       → Cache response (nếu có [Cacheable])
└── AutoMapper profiles (auto-scan assembly)
```

> **⚠️ Thứ tự Pipeline Behaviors rất quan trọng!** Request đi qua Logging → Validation → Authorization → Caching → Handler.

#### ⑦ `AddInfrastructureServices` — Infrastructure Layer

```
📦 Đăng ký:
├── IUnitOfWork → UnitOfWork (Scoped)
├── IGenericRepository<,> → GenericRepository<,> (Scoped, Open Generic)
├── IPostQueryService → PostQueryService (Scoped, Dapper queries)
├── IDateTimeService → DateTimeService (Singleton)
├── IJwtService → JwtService (Scoped)
├── ITokenBlacklistService → TokenBlacklistService (Scoped)
├── INotificationService → NotificationService (Scoped)
├── IFileService → FileService (Scoped)
└── IChatService → ChatService (Scoped)
```

#### ⑧ `AddCorsPolicy` — CORS

Cho phép tất cả origin (development mode), cho phép Credentials cho WebSocket/SignalR.

### Bước 2.4 — Cấu hình bổ sung

```csharp
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Bỏ qua circular reference khi serialize JSON
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddOpenApiDocumentation();  // Swagger/Scalar UI
builder.Services.AddHealthChecks();          // Health check endpoint
```

### Bước 2.5 — Build & Cấu hình Middleware Pipeline

```csharp
var app = builder.Build();
```

Sau khi build, cấu hình thứ tự middleware (rất quan trọng!):

```
Request đi vào ──────────────────────────────────────────────► Response đi ra
    │                                                              ▲
    ▼                                                              │
┌──────────────────────────────────────────────────────────────────┐
│ 1. ExceptionHandlingMiddleware ← Bắt mọi exception             │
├──────────────────────────────────────────────────────────────────┤
│ 2. SerilogRequestLogging ← Ghi log mỗi HTTP request            │
├──────────────────────────────────────────────────────────────────┤
│ 3. HealthChecks (/health) ← Trả 200 OK nếu app sống           │
├──────────────────────────────────────────────────────────────────┤
│ 4. Swagger/Scalar ← Serve API docs                             │
├──────────────────────────────────────────────────────────────────┤
│ 5. UseRouting ← Xác định route nào khớp                        │
├──────────────────────────────────────────────────────────────────┤
│ 6. UseCors ← Kiểm tra CORS headers                             │
├──────────────────────────────────────────────────────────────────┤
│ 7. UseIpRateLimiting ← Chặn nếu vượt limit (không có ở Test)  │
├──────────────────────────────────────────────────────────────────┤
│ 8. UseAuthentication ← Giải mã JWT token → ClaimsPrincipal     │
├──────────────────────────────────────────────────────────────────┤
│ 9. TokenBlacklistMiddleware ← Kiểm tra token bị thu hồi        │
├──────────────────────────────────────────────────────────────────┤
│ 10. UseAuthorization ← Kiểm tra [Authorize] attribute          │
├──────────────────────────────────────────────────────────────────┤
│ 11. Endpoint Execution (Controller Action / SignalR Hub)        │
└──────────────────────────────────────────────────────────────────┘
```

### Bước 2.6 — Map Endpoints

```csharp
app.MapControllers();                                        // REST API
app.MapHub<ChatHub>("/hubs/chat");                           // WebSocket Chat
app.MapHub<NotificationHub>("/hubs/notifications");          // WebSocket Notifications
```

### Bước 2.7 — Database Initialization & Seeding

```csharp
// 1. Luôn chạy: Tạo/cập nhật schema bằng EF Migrations
await app.InitializeDatabaseAsync();

// 2. Chỉ seed data khi Development HOẶC biến env SEED_DATA=true
if (app.Environment.IsDevelopment() || Environment.GetEnvironmentVariable("SEED_DATA") == "true")
{
    await app.SeedDatabaseAsync();
}
```

**Seed data bao gồm:**
- Roles: `Admin`, `User`
- Admin user: `admin@floracore.com` / `Admin123!`
- Product categories: Technology, Audio
- Sample products: Smartphone X, Laptop Pro 16, Wireless Buds
- Sample posts: 3 bài blog

### Bước 2.8 — Chạy ứng dụng

```csharp
app.Run();  // Bắt đầu lắng nghe HTTP request trên port configured
```

---

## 3. Luồng Xử Lý Request (Request Pipeline)

Khi một HTTP request đến, nó đi qua **Middleware Pipeline** theo thứ tự. Dưới đây là chi tiết từng bước:

### 📊 Sơ đồ xử lý request

```mermaid
sequenceDiagram
    participant Client
    participant ExceptionMW as Exception Middleware
    participant SerilogMW as Serilog Logging
    participant RateLimitMW as Rate Limiter
    participant AuthMW as Authentication
    participant BlacklistMW as Token Blacklist
    participant AuthzMW as Authorization
    participant Router as Routing
    participant Controller as Controller
    participant MediatR as MediatR Pipeline

    Client->>ExceptionMW: HTTP Request
    ExceptionMW->>SerilogMW: try { next() }
    SerilogMW->>RateLimitMW: Log request info
    RateLimitMW->>AuthMW: Check IP rate limit
    
    alt Rate limit exceeded
        RateLimitMW-->>Client: 429 Too Many Requests
    end
    
    AuthMW->>BlacklistMW: Decode JWT → ClaimsPrincipal
    BlacklistMW->>AuthzMW: Check token JTI
    
    alt Token blacklisted
        BlacklistMW-->>Client: 401 Unauthorized
    end
    
    AuthzMW->>Router: Check [Authorize] attribute
    Router->>Controller: Match route → invoke action
    Controller->>MediatR: mediator.Send(command/query)
    MediatR-->>Controller: Result
    Controller-->>Client: HTTP Response (200/400/404...)
    
    Note over ExceptionMW: Nếu có exception bất kỳ,<br/>ExceptionMW bắt và trả JSON error
```

### 3.1 — ExceptionHandlingMiddleware (Bước đầu tiên)

> **File:** `Middleware/ExceptionHandlingMiddleware.cs`

**Vai trò:** Bọc toàn bộ pipeline trong `try-catch`, đảm bảo client LUÔN nhận JSON response thay vì stack trace.

**Bảng ánh xạ Exception → HTTP Status:**

| Exception Type | HTTP Status | Response |
|---|---|---|
| `ValidationException` | **400** Bad Request | `{ errors: [{ propertyName, errorMessage }] }` |
| `EntityNotFoundException` | **404** Not Found | `{ message: "..." }` |
| `KeyNotFoundException` | **404** Not Found | `{ message: "..." }` |
| `FileNotFoundException` | **404** Not Found | `{ message: "..." }` |
| `UnauthorizedAccessException` | **403** Forbidden | `{ message: "Forbidden" }` |
| `AccessDeniedException` | **403** Forbidden | `{ message: "..." }` |
| `DomainException` | **400** Bad Request | `{ message: "..." }` |
| `ArgumentException` | **400** Bad Request | `{ message: "..." }` |
| Mọi exception khác | **500** Internal Server Error | `{ message: "Internal Server Error" }` |

> **Lưu ý:** Trong môi trường **Development** hoặc **Testing**, response 500 sẽ bao gồm thêm `stackTrace` để debug.

### 3.2 — Serilog Request Logging

Tự động ghi log mỗi HTTP request với thông tin: Method, Path, Status Code, Duration.

```
[INF] HTTP GET /api/posts responded 200 in 45.2ms
```

### 3.3 — Rate Limiting

Kiểm tra IP của client, nếu vượt quá số lượng request cho phép → trả `429 Too Many Requests`.

- **Mọi endpoint:** Tối đa 100 request/phút
- **POST /api/auth/login:** Tối đa 5 request/phút (chống brute force)
- **IP Whitelist:** `127.0.0.1`, `::1` (localhost không bị limit)

> Rate limiting bị **tắt** trong môi trường `Testing`.

### 3.4 — Authentication (JWT)

ASP.NET tự động:
1. Đọc header `Authorization: Bearer <token>`
2. Validate JWT (signature, issuer, audience, expiry)
3. Tạo `ClaimsPrincipal` từ claims trong token
4. Gán vào `HttpContext.User`

**Đặc biệt cho SignalR:** Token có thể đến qua query string `?access_token=<token>` (vì WebSocket không hỗ trợ custom headers).

### 3.5 — TokenBlacklistMiddleware

> **File:** `Middleware/TokenBlacklistMiddleware.cs`

**Vai trò:** Kiểm tra xem JWT token có bị **thu hồi** (blacklisted) hay không.

**Luồng:**
1. Đọc token từ header `Authorization`
2. Parse JWT để lấy claim `jti` (JWT ID)
3. Kiểm tra `jti` trong blacklist (thông qua `ITokenBlacklistService`)
4. Nếu bị blacklist → trả `401 Unauthorized` + `"Token is blacklisted"`
5. Nếu không → cho request đi tiếp

**Khi nào token bị blacklist?** Khi user gọi `POST /api/auth/logout`.

### 3.6 — Authorization

ASP.NET kiểm tra `[Authorize]` attribute trên Controller/Action:
- Nếu có `[Authorize]` mà không có valid token → `401 Unauthorized`
- Nếu có `[AllowAnonymous]` → bỏ qua kiểm tra
- Nếu có `[Authorize(Roles = "Admin")]` → kiểm tra role

### 3.7 — Controller Action

Sau tất cả middleware, request đến Controller. Controller **không chứa business logic**, mà chỉ:
1. Nhận parameters từ request (query, body, route)
2. Tạo Command/Query object
3. Gửi qua `IMediator.Send()`
4. Trả HTTP response

```csharp
// Ví dụ: PostsController.Create()
[HttpPost]
[Authorize]
public async Task<IActionResult> Create([FromBody] CreatePostCommand command)
{
    var id = await _mediator.Send(command);  // ← Gửi vào MediatR pipeline
    return Ok(id);
}
```

---

## 4. Luồng MediatR Pipeline (CQRS)

Khi `_mediator.Send(request)` được gọi, request đi qua **4 Pipeline Behaviors** trước khi đến Handler:

### 📊 Sơ đồ MediatR Pipeline

```mermaid
flowchart LR
    A["Controller<br/>mediator.Send()"] --> B["① LoggingBehavior"]
    B --> C["② ValidationBehavior"]
    C --> D["③ AuthorizationBehavior"]
    D --> E["④ CachingBehavior"]
    E --> F["⑤ Handler<br/>(Business Logic)"]
    F --> E
    E --> D
    D --> C
    C --> B
    B --> A

    style A fill:#1a1a2e,stroke:#e94560,color:#fff
    style B fill:#16213e,stroke:#0f3460,color:#fff
    style C fill:#16213e,stroke:#0f3460,color:#fff
    style D fill:#16213e,stroke:#0f3460,color:#fff
    style E fill:#16213e,stroke:#0f3460,color:#fff
    style F fill:#0f3460,stroke:#e94560,color:#fff
```

### ① LoggingBehavior

> **File:** `Application/Common/Behaviors/LoggingBehavior.cs`

Ghi log **trước** và **sau** mỗi request:

```
[INF] Handling CreatePostCommand
... (xử lý bên trong) ...
[INF] Handled CreatePostCommand
```

### ② ValidationBehavior

> **File:** `Application/Common/Behaviors/ValidationBehavior.cs`

**Luồng:**
1. Tìm tất cả `IValidator<TRequest>` đã đăng ký cho request type
2. Nếu có validator → chạy tất cả song song (`Task.WhenAll`)
3. Thu thập lỗi từ tất cả validators
4. Nếu có lỗi → `throw ValidationException(failures)` → ExceptionMiddleware bắt → trả `400`
5. Nếu không lỗi → cho đi tiếp

**Ví dụ validator:**

```csharp
// LoginCommandValidator.cs
public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
    }
}
```

### ③ AuthorizationBehavior

> **File:** `Application/Common/Behaviors/AuthorizationBehavior.cs`

**Luồng:**
1. Kiểm tra request có implement `IOwnershipRequest` không
2. Nếu có → lấy `UserId` từ `ICurrentUserService`
3. Dùng `IGenericRepository<Post, Guid>` để lấy Post theo `Id`
4. So sánh `post.AuthorId` với `userId`
5. Nếu không phải owner → `throw UnauthorizedAccessException`

**Áp dụng cho:** `UpdatePostCommand`, `DeletePostCommand` (các request implement `IOwnershipRequest`).

### ④ CachingBehavior

> **File:** `Application/Common/Behaviors/CachingBehavior.cs`

**Luồng:**
1. Kiểm tra request class có `[Cacheable]` attribute không
2. Nếu không → bỏ qua, gọi handler trực tiếp
3. Nếu có:
   a. Tạo cache key: `Cache_{RequestType}_{SerializedRequest}`
   b. Kiểm tra `IDistributedCache` có cache cho key này chưa
   c. **Cache HIT** → deserialize và trả về, KHÔNG gọi handler
   d. **Cache MISS** → gọi handler, lưu kết quả vào cache với TTL từ attribute

```csharp
// Ví dụ sử dụng attribute
[Cacheable(ExpirationMinutes = 10)]
public class GetPostsQuery : IRequest<CursorPagedList<PostDto>>
{
    // ...
}
```

### ⑤ Handler (Business Logic)

Đây là nơi xử lý business logic thực sự. Mỗi Command/Query có **đúng 1 Handler**.

```csharp
// Ví dụ: CreatePostCommandHandler
public async Task<Guid> Handle(CreatePostCommand request, CancellationToken cancellationToken)
{
    var post = new Post
    {
        Title = request.Title,
        Content = request.Content,
        AuthorId = _currentUser.UserId!.Value,
        // ...
    };
    
    await _repository.AddAsync(post);
    await _unitOfWork.SaveChangesAsync(cancellationToken);
    
    return post.Id;
}
```

---

## 5. Ví Dụ Cụ Thể

### 🔐 Ví dụ 1: POST /api/auth/login

```
Client gửi: POST /api/auth/login
Body: { "email": "admin@floracore.com", "password": "Admin123!" }

1. ExceptionHandlingMiddleware     → try { next() }
2. SerilogRequestLogging           → Bắt đầu ghi log
3. Rate Limiting                   → Kiểm tra: ≤5 request/phút cho login? ✅
4. Authentication                  → Không có Bearer token → User = Anonymous ✅
5. TokenBlacklist                  → Không có token → bỏ qua ✅
6. Authorization                   → Không có [Authorize] trên action → bỏ qua ✅
7. AuthController.Login()          → _mediator.Send(LoginCommand)
   │
   ├── LoggingBehavior             → [INF] Handling LoginCommand
   ├── ValidationBehavior          → Kiểm tra email hợp lệ & password ≥ 6 ký tự ✅
   ├── AuthorizationBehavior       → LoginCommand không có IOwnershipRequest → bỏ qua
   ├── CachingBehavior             → LoginCommand không có [Cacheable] → bỏ qua
   └── LoginCommandHandler         → Xác thực user → Tạo JWT + Refresh Token
       └── Return: AuthResponse { AccessToken, RefreshToken, Expiry }
   │
8. Controller trả về               → 200 OK + AuthResponse JSON
9. SerilogRequestLogging            → [INF] HTTP POST /api/auth/login responded 200 in 120ms
```

### 📝 Ví dụ 2: POST /api/posts (Tạo bài viết mới)

```
Client gửi: POST /api/posts
Headers: Authorization: Bearer eyJhbGciOi...
Body: { "title": "My Post", "content": "Hello World", "categoryId": "blog" }

1. ExceptionHandlingMiddleware     → try { next() }
2. SerilogRequestLogging           → Bắt đầu ghi log
3. Rate Limiting                   → Kiểm tra: ≤100 request/phút? ✅
4. Authentication                  → Decode JWT → ClaimsPrincipal { sub: "guid-abc", role: "User" }
5. TokenBlacklist                  → Kiểm tra JTI trong blacklist → Không có → ✅
6. Authorization                   → [Authorize] trên action + User đã xác thực → ✅
7. PostsController.Create()        → _mediator.Send(CreatePostCommand)
   │
   ├── LoggingBehavior             → [INF] Handling CreatePostCommand
   ├── ValidationBehavior          → Kiểm tra Title không rỗng, Content không rỗng ✅
   ├── AuthorizationBehavior       → Không phải IOwnershipRequest → bỏ qua
   ├── CachingBehavior             → Không có [Cacheable] → bỏ qua
   └── CreatePostCommandHandler    → Tạo Post entity → Save vào DB
       └── Return: Guid (Post ID)
   │
8. Controller trả về               → 200 OK + "guid-new-post"
9. SerilogRequestLogging            → [INF] HTTP POST /api/posts responded 200 in 85ms
```

### 🔍 Ví dụ 3: GET /api/posts (Lấy danh sách - có Cache)

```
Client gửi: GET /api/posts?pageSize=10

(Các bước middleware tương tự - không cần auth vì không có [Authorize])

7. PostsController.GetAll()        → _mediator.Send(GetPostsQuery)
   │
   ├── LoggingBehavior             → [INF] Handling GetPostsQuery
   ├── ValidationBehavior          → Không có validator → bỏ qua
   ├── AuthorizationBehavior       → Không phải IOwnershipRequest → bỏ qua
   ├── CachingBehavior             → Kiểm tra [Cacheable] attribute
   │   ├── Cache Key: "Cache_GetPostsQuery_{...serialized...}"
   │   ├── LẦN 1 (Cache MISS):
   │   │   ├── Gọi Handler → Truy vấn DB → Kết quả
   │   │   └── Lưu kết quả vào IDistributedCache (TTL: X phút)
   │   └── LẦN 2+ (Cache HIT):
   │       └── Trả về từ cache → KHÔNG gọi Handler → Nhanh hơn rất nhiều 🚀
   └── GetPostsQueryHandler        → Dùng IPostQueryService (Dapper) → Truy vấn DB
```

### ❌ Ví dụ 4: Request bị lỗi Validation

```
Client gửi: POST /api/auth/register
Body: { "email": "invalid-email", "password": "12" }

...middleware bình thường...

7. AuthController.Register()       → _mediator.Send(RegisterCommand)
   │
   ├── LoggingBehavior             → [INF] Handling RegisterCommand
   ├── ValidationBehavior          → 🚨 Kiểm tra thất bại!
   │   ├── Email: phải là email hợp lệ
   │   └── Password: phải ≥ 6 ký tự
   │   └── throw ValidationException(failures)
   │
8. Exception bubble lên qua pipeline
9. ExceptionHandlingMiddleware bắt → Trả 400:
   {
     "errors": [
       { "propertyName": "Email", "errorMessage": "..." },
       { "propertyName": "Password", "errorMessage": "..." }
     ]
   }
```

---

## 6. Sơ Đồ Tổng Hợp

### Toàn bộ luồng từ Request đến Response

```mermaid
flowchart TB
    subgraph CLIENT["🌐 Client"]
        REQ["HTTP Request"]
        RES["HTTP Response"]
    end

    subgraph MIDDLEWARE["⚙️ ASP.NET Middleware Pipeline"]
        direction TB
        M1["ExceptionHandlingMiddleware<br/>Bắt mọi exception → JSON"]
        M2["Serilog Request Logging<br/>Ghi log HTTP request"]
        M3["Rate Limiting<br/>Chống spam/DDoS"]
        M4["Authentication<br/>JWT → ClaimsPrincipal"]
        M5["TokenBlacklist<br/>Kiểm tra token thu hồi"]
        M6["Authorization<br/>Kiểm tra quyền truy cập"]
    end

    subgraph CONTROLLER["🎯 Controller"]
        CTRL["PostsController<br/>AuthController<br/>..."]
    end

    subgraph MEDIATR["🔄 MediatR Pipeline"]
        direction TB
        B1["LoggingBehavior<br/>Log trước/sau"]
        B2["ValidationBehavior<br/>FluentValidation"]
        B3["AuthorizationBehavior<br/>Kiểm tra ownership"]
        B4["CachingBehavior<br/>Distributed Cache"]
        HANDLER["Handler<br/>Business Logic"]
    end

    subgraph INFRA["🏗️ Infrastructure"]
        REPO["GenericRepository<br/>PostQueryService"]
        UOW["UnitOfWork"]
        DB[("Database<br/>SQL Server / PostgreSQL")]
        CACHE[("Distributed Cache<br/>In-Memory")]
        HUB["SignalR Hubs<br/>Chat & Notifications"]
    end

    REQ --> M1 --> M2 --> M3 --> M4 --> M5 --> M6
    M6 --> CTRL
    CTRL --> B1 --> B2 --> B3 --> B4 --> HANDLER
    HANDLER --> REPO --> UOW --> DB
    B4 -.->|Cache Hit/Miss| CACHE
    HANDLER -.->|Real-time events| HUB
    HANDLER --> CTRL --> RES

    style CLIENT fill:#1a1a2e,stroke:#e94560,color:#fff
    style MIDDLEWARE fill:#16213e,stroke:#0f3460,color:#fff
    style CONTROLLER fill:#0f3460,stroke:#53354a,color:#fff
    style MEDIATR fill:#533483,stroke:#e94560,color:#fff
    style INFRA fill:#2b2d42,stroke:#8d99ae,color:#fff
```

### Cấu trúc thư mục ánh xạ kiến trúc

```
FloraCore/
├── 📄 Program.cs                          ← Entry point, cấu hình DI & Middleware
├── 📁 Controllers/                        ← Presentation Layer
│   ├── AuthController.cs                  ← /api/auth/*
│   ├── CartController.cs                  ← /api/cart/*
│   ├── ChatController.cs                  ← /api/chat/*
│   ├── FavoritesController.cs             ← /api/favorites/*
│   ├── FilesController.cs                 ← /api/files/*
│   ├── NotificationsController.cs         ← /api/notifications/*
│   ├── OrdersController.cs                ← /api/orders/*
│   ├── PaymentsController.cs              ← /api/payments/*
│   ├── PostCategoriesController.cs        ← /api/postcategories/*
│   ├── PostsController.cs                 ← /api/posts/*
│   ├── ProductCategoriesController.cs     ← /api/productcategories/*
│   ├── ProductsController.cs              ← /api/products/*
│   ├── ReviewsController.cs               ← /api/reviews/*
│   ├── UsersController.cs                 ← /api/users/*
│   ├── VerifyController.cs                ← /api/verify/*
│   └── WebsiteInfoController.cs           ← /api/websiteinfo/*
├── 📁 Middleware/                          ← Custom Middleware
│   ├── ExceptionHandlingMiddleware.cs     ← Xử lý exception toàn cục
│   └── TokenBlacklistMiddleware.cs        ← Kiểm tra token thu hồi
├── 📁 Application/                        ← Application Layer
│   ├── 📁 Common/
│   │   ├── 📁 Behaviors/                 ← MediatR Pipeline Behaviors
│   │   │   ├── LoggingBehavior.cs
│   │   │   ├── ValidationBehavior.cs
│   │   │   ├── AuthorizationBehavior.cs
│   │   │   └── CachingBehavior.cs
│   │   ├── 📁 Interfaces/               ← Abstractions (DI contracts)
│   │   ├── 📁 Mappings/                 ← AutoMapper profiles
│   │   ├── 📁 Models/                   ← Shared DTOs & Models
│   │   └── 📁 Extensions/              ← DI registration extensions
│   └── 📁 Features/                      ← CQRS Commands & Queries
│       ├── 📁 Auth/                      ← Xác thực và tài khoản
│       ├── 📁 Cart/                      ← Giỏ hàng mua sắm
│       ├── 📁 Chat/                      ← Nhắn tin real-time
│       ├── 📁 Favorites/                 ← Danh sách yêu thích
│       ├── 📁 Notifications/             ← Thông báo người dùng
│       ├── 📁 Orders/                    ← Đơn hàng và thống kê
│       ├── 📁 PostCategories/            ← Danh mục bài viết
│       ├── 📁 Posts/                     ← Quản lý bài viết (blog)
│       ├── 📁 ProductCategories/         ← Danh mục sản phẩm
│       ├── 📁 Products/                  ← Quản lý sản phẩm
│       ├── 📁 Reviews/                   ← Đánh giá sản phẩm
│       ├── 📁 Users/                     ← Quản lý người dùng
│       └── 📁 WebsiteInfo/               ← Cấu hình thông tin Website
├── 📁 Infrastructure/                     ← Infrastructure Layer
│   ├── 📁 Data/
│   │   ├── AppDbContext.cs              ← EF Core DbContext
│   │   ├── UnitOfWork.cs                ← Transaction management
│   │   └── DatabaseSeeder.cs            ← Seed data + DB init
│   ├── 📁 Repositories/                ← GenericRepository, PostQueryService
│   ├── 📁 Services/                    ← JwtService, FileService, etc.
│   └── 📁 Hubs/                        ← SignalR Hubs (Chat, Notification)
├── 📁 Domain/                            ← Domain Layer (innermost)
│   ├── 📁 Entities/                     ← Post, AppUser, Product, etc.
│   ├── 📁 Exceptions/                  ← Domain exceptions
│   └── 📁 ValueObjects/                ← Value objects
└── 📁 Migrations/                        ← EF Core Migrations
```

---

## 📌 Tóm Tắt Nhanh

| Giai đoạn | Thành phần | Vai trò |
|---|---|---|
| **Startup** | `Program.cs` | Đăng ký DI, cấu hình middleware, khởi tạo DB |
| **Middleware** | `ExceptionHandling`, `Serilog`, `RateLimit`, `Auth`, `Blacklist` | Xử lý cross-cutting concerns |
| **Routing** | ASP.NET Routing + Controllers | Map URL → Controller Action |
| **CQRS** | MediatR + Behaviors | Tách biệt command/query, cross-cutting pipeline |
| **Validation** | FluentValidation + ValidationBehavior | Tự động validate input |
| **Caching** | IDistributedCache + CachingBehavior | Cache response query |
| **Data Access** | EF Core + GenericRepo + UnitOfWork | CRUD + Transaction |
| **Real-time** | SignalR Hubs | WebSocket chat & notifications |

---

> **Tài liệu này được tạo dựa trên mã nguồn thực tế của project FloraCore.**
> Cập nhật lần cuối: 2026-03-13
