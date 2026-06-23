# 📘 FloraCore Clean Architecture - Technical Reference Guide

Tài liệu này cung cấp hướng dẫn chi tiết về cấu trúc kiến trúc, các nguyên tắc thiết kế, các mẫu thiết kế (Design Patterns) và các quy chuẩn lập trình được áp dụng trong dự án **FloraCore**. Mục tiêu là giúp các kỹ sư phát triển hiểu sâu sắc về ranh giới hệ thống, lý do đằng sau các lựa chọn thiết kế, và cách triển khai các tính năng mới mà không phá vỡ cấu trúc Clean Architecture.

---

## 📑 Mục lục

1. [Kiến trúc Tổng quan & Động lực (Architecture Overview & Motivation)](#1-kiến-trúc-tổng-quan--động-lực)
2. [Quy tắc Phụ thuộc & SOLID Principles](#2-quy-tắc-phụ-thuộc--solid-principles)
3. [Chi tiết các Layer trong FloraCore](#3-chi-tiết-các-layer-trong-floracore)
4. [Các Mẫu Thiết kế Cốt lõi (Core Patterns)](#4-các-mẫu-thiết-kế-cốt-lõi)
5. [Góc Nhìn Chuyên sâu & Lưu ý Kiến trúc (Deep Insights)](#5-góc-nhìn-chuyên-sâu--lưu-ý-kiến-trúc)
6. [Quy chuẩn C# 12+ & Coding Best Practices](#6-quy-chuẩn-c-12--coding-best-practices)
7. [Chiến lược Kiểm thử (Testing Strategy)](#7-chiến-lược-kiểm-thử)
8. [Các Lỗi Kiến trúc Thường gặp (Anti-Patterns)](#8-các-lỗi-kiến-trúc-thường-gặp)

---

## 1. Kiến trúc Tổng quan & Động lực

### Tại sao sử dụng Clean Architecture?
Dự án FloraCore áp dụng kiến trúc **Clean Architecture** (được giới thiệu bởi Robert C. Martin - Uncle Bob) nhằm đạt được 5 mục tiêu cốt lõi:
1. **Độc lập với Framework**: Frameworks (như ASP.NET Core) chỉ đóng vai trò là chi tiết triển khai (implementation details). Business logic không bị ràng buộc hay phụ thuộc vào các thư viện bên ngoài.
2. **Khả năng Kiểm thử cao (Testability)**: Business rules cốt lõi có thể được kiểm thử độc lập mà không cần khởi chạy Database, Web Server, UI hay bất kỳ dịch vụ bên ngoài nào khác.
3. **Độc lập với Giao diện (UI)**: Dễ dàng chuyển đổi hoặc hỗ trợ đồng thời nhiều dạng Presentation (Web API, Razor Pages, CLI, Telegram/Zalo Bot) mà không ảnh hưởng tới Business Logic.
4. **Độc lập với Database**: Ranh giới truy xuất dữ liệu được định nghĩa bằng các abstraction (Interfaces). Việc thay đổi hệ quản trị cơ sở dữ liệu (từ SQL Server sang PostgreSQL) chỉ ảnh hưởng tới tầng Infrastructure.
5. **Độc lập với Dịch vụ bên ngoài (External Services)**: Các tích hợp bên ngoài (như cổng thanh toán VNPAY/MoMo, dịch vụ gửi email MailKit, Cloudinary) được đặt phía sau các cổng (Gateways) định nghĩa ở tầng Application.

```
                  ┌─────────────────────────────────────┐
                  │          Presentation (API)         │
                  │  (Controllers, Middlewares, Hubs)   │
                  └──────────────────┬──────────────────┘
                                     │ calls
                                     ▼
                  ┌─────────────────────────────────────┐
                  │             Application             │
                  │   (Use Cases, Commands, Queries)    │
                  └──────────────────┬──────────────────┘
                                     │ orchestrates
                                     ▼
                  ┌─────────────────────────────────────┐
                  │               Domain                │
                  │   (Entities, Rules, Value Objects)  │
                  └─────────────────────────────────────┘
                                     ▲
                                     │ implements interfaces
                  ┌──────────────────┴──────────────────┐
                  │           Infrastructure            │
                  │  (DbContext, Repositories, Dapper)  │
                  └─────────────────────────────────────┘
```

> [!NOTE]
> **Ranh giới vật lý (Physical Boundaries)**:
> FloraCore thực hiện phân tách ranh giới các layer bằng các project `.csproj` riêng biệt. Việc này ngăn chặn triệt để tình trạng lập trình viên vô ý add tham chiếu ngược từ các tầng lõi ra tầng ngoài.

---

## 2. Quy tắc Phụ thuộc & SOLID Principles

### Dependency Rule (Quy tắc Phụ thuộc)
> **Source code dependencies must point only INWARD, toward higher-level policies.**
> (Các dependency trong mã nguồn chỉ được phép hướng VÀO TRONG, về phía các chính sách cấp cao hơn).

- Tầng trong **không bao giờ** được biết bất kỳ thông tin nào về tầng ngoài.
- Các class thuộc tầng ngoài chỉ giao tiếp với tầng trong thông qua các **Interfaces** (Abstractions) được định nghĩa ở tầng Application hoặc Domain.

| Layer nguồn | Layer đích hợp lệ | Ví dụ hợp lệ |
| :--- | :--- | :--- |
| **Presentation** | Application, Domain | controller gọi `IMediator.Send(command)` |
| **Infrastructure** | Application, Domain | `ProductRepository` implement `IProductRepository` |
| **Application** | Domain | `CreatePostHandler` tạo entity `Post` và lưu qua `IGenericRepository<Post, Guid>` |
| **Domain** | Không phụ thuộc | Chỉ chứa kiểu dữ liệu cơ bản, Entities, Value Objects |

### SOLID Principles trong Kiến trúc
* **S (Single Responsibility)**: Mỗi Handler chỉ giải quyết một Use Case duy nhất (Command hoặc Query). Mỗi class Service đảm nhận một trách nhiệm kỹ thuật duy nhất.
* **O (Open/Closed)**: Dễ dàng mở rộng hành vi (ví dụ: thêm cổng thanh toán PayOS) bằng cách tạo class mới implement `IPaymentService` thay vì sửa đổi logic xử lý thanh toán hiện tại.
* **L (Liskov Substitution)**: Các client sử dụng `IGenericRepository<TEntity, TKey>` chạy bình thường với bất kỳ triển khai thực tế nào (EF Core, In-Memory mock).
* **I (Interface Segregation)**: Chia nhỏ interfaces. Tránh việc tạo các interface quá lớn (fat interface). Ví dụ, tách biệt `IPostQueryService` cho tác vụ đọc bằng Dapper ra khỏi các repository ghi.
* **D (Dependency Inversion)**: Các module cấp cao (Application/Domain) không phụ thuộc vào các module cấp thấp (Infrastructure). Cả hai đều phụ thuộc vào abstractions (Interfaces).

---

## 3. Chi tiết các Layer trong FloraCore

### 3.1. Domain Layer (Lõi Hệ thống)
Chứa các thành phần cốt lõi đại diện cho nghiệp vụ của FloraCore. Layer này hoàn toàn độc lập và không phụ thuộc vào bất kỳ thư viện bên ngoài nào ngoại trừ các kiểu dữ liệu cơ bản của C#.

* **Entities**: Các đối tượng nghiệp vụ có định danh (Identity) duy nhất (ví dụ: `Post`, `Product`, `Order`). Entities chứa dữ liệu nghiệp vụ và các phương thức thay đổi trạng thái kèm validation nghiệp vụ.
* **Value Objects**: Đối tượng không có định danh, đại diện cho một nhóm các thuộc tính kết hợp (ví dụ: `Money` gồm Amount và Currency). Chúng là bất biến (immutable) và so sánh bằng giá trị của thuộc tính.
* **Domain Events**: Sự kiện mô tả một hành động nghiệp vụ quan trọng vừa xảy ra trong Domain (ví dụ: `OrderCreatedEvent`), dùng để giao tiếp bất đồng bộ giữa các Aggregate.

**Ví dụ Entity với Business Logic tự đóng gói:**
```csharp
namespace FloraCore.Domain.Entities;

public class Post
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Guid AuthorId { get; set; }
    
    public double AverageRating { get; private set; }
    public int TotalRatings { get; private set; }
    
    // Business logic được đóng gói trực tiếp trong Entity (Rich Domain Model)
    public void AddRating(int score)
    {
        if (score < 1 || score > 5) 
            throw new ArgumentOutOfRangeException(nameof(score), "Rating must be between 1 and 5");
        
        double currentTotalScore = AverageRating * TotalRatings;
        TotalRatings++;
        AverageRating = (currentTotalScore + score) / TotalRatings;
    }
}
```

---

### 3.2. Application Layer (Nghiệp vụ Use Case)
Định nghĩa các luồng nghiệp vụ của hệ thống (Use Cases) bằng cách điều phối các Domain Entities và sử dụng các Abstraction định nghĩa sẵn.

* **Features (Vertical Slices)**: Tổ chức mã nguồn theo chức năng nghiệp vụ (Feature Folders) thay vì phân nhóm kỹ thuật. Mỗi Feature chứa:
  * **Commands**: Tác vụ thay đổi trạng thái (Create, Update, Delete).
  * **Queries**: Tác vụ truy vấn dữ liệu (Get, Search).
  * **DTOs**: Đối tượng trung chuyển dữ liệu tối ưu cho Response.
  * **Validators**: Validation dữ liệu đầu vào sử dụng `FluentValidation`.
* **Common/Interfaces**: Các hợp đồng dịch vụ hệ thống như `IUnitOfWork`, `IGenericRepository`, `IPostQueryService`, `IEmailService`.
* **Common/Behaviors**: Các bộ lọc chung xử lý xuyên suốt (Cross-cutting concerns) tích hợp vào MediatR Pipeline (Logging, Validation, Caching).

```
Application/
├── Common/
│   ├── Interfaces/          # Interfaces dùng chung
│   │   ├── IGenericRepository.cs
│   │   ├── IUnitOfWork.cs
│   │   ├── IPostQueryService.cs
│   │   └── IEmailService.cs
│   ├── Behaviors/           # MediatR Behaviors
│   │   ├── ValidationBehavior.cs
│   │   └── LoggingBehavior.cs
│   └── Models/              # DTOs dùng chung
│       ├── PagedResult.cs
│       └── Result.cs
├── Interfaces/              # Interfaces đặc thù cho các Entity cụ thể
│   ├── IProductRepository.cs
│   └── IOrderRepository.cs
└── Features/                # Chia thư mục theo chức năng nghiệp vụ nghiệp vụ (Vertical Slices)
    ├── Posts/
    │   ├── Commands/
    │   │   ├── CreatePostCommand.cs
    │   │   └── CreatePostCommandValidator.cs
    │   ├── Queries/
    │   │   ├── GetPostsQuery.cs
    │   │   └── GetPostDetailQuery.cs
    │   └── DTOs/
    │       └── PostDto.cs
    └── Products/ ...
```

---

### 3.3. Infrastructure Layer (Triển khai Kỹ thuật)
Chứa các triển khai cụ thể cho các dịch vụ được yêu cầu bởi Application Layer.

* **Data**: `AppDbContext` kế thừa từ EF Core DbContext cấu hình ánh xạ cơ sở dữ liệu và triển khai `IUnitOfWork`.
* **Repositories**: Triển khai các Repository thao tác với cơ sở dữ liệu (`GenericRepository<TEntity, TKey>`, `ProductRepository`, `OrderRepository`).
* **Services**: Triển khai tích hợp với các API hoặc dịch vụ hạ tầng (`JwtService`, `EmailService` sử dụng MailKit, `MoMoService`, `VnPayService`).
* **Hubs**: Real-time communication sử dụng SignalR (được coi là một chi tiết hạ tầng).

---

### 3.4. Presentation Layer (API/Giao diện)
Cổng vào của ứng dụng. Nhiệm vụ chính là tiếp nhận HTTP Request, chuyển đổi dữ liệu đầu vào, gửi lệnh vào Application Layer thông qua MediatR, nhận kết quả và định dạng lại HTTP Response.

* **Controllers**: Các API Controller gọn nhẹ (Thin Controllers) không chứa business logic.
* **Middlewares**: Các bộ lọc bắt lỗi hệ thống (`ExceptionHandlingMiddleware`), ghi log request.
* **Program.cs**: Cấu hình khởi tạo Host, thiết lập Middleware Pipeline và gọi đăng ký DI ở các Layer ngoài.

---

## 4. Các Mẫu Thiết kế Cốt lõi (Core Patterns)

### 4.1. CQRS kết hợp MediatR
FloraCore tách biệt hoàn toàn luồng ghi (Commands) và luồng đọc (Queries):
- **Commands**: Được xử lý thông qua EF Core và lưu trữ thông qua `IGenericRepository<TEntity, TKey>`.
- **Queries**: Được tối ưu hóa hiệu năng, truy vấn trực tiếp dữ liệu dạng Read-only thông qua Dapper và `IPostQueryService`.
- **MediatR**: Đóng vai trò là cầu nối trung gian, giảm coupling giữa các Presentation Controller và Application Handlers.

```
[HTTP POST] ──► Controller ──► MediatR.Send(Command) ──► CommandHandler ──► EF Core Write ──► Database
                                                                                                 │
[HTTP GET]  ──► Controller ──► MediatR.Send(Query)   ──► QueryHandler   ──► Dapper Read   ◄──────┘
```

**Mẫu triển khai Command Handler (Ghi):**
```csharp
namespace FloraCore.Application.Features.Posts.Commands;

using FloraCore.Application.Common.Interfaces;
using FloraCore.Domain.Entities;
using MediatR;

public record CreatePostCommand(Guid? Id, string Title, string Content, string? CategoryId = null) : IRequest<Guid>;

public class CreatePostHandler(
    IGenericRepository<Post, Guid> postRepository, 
    ICurrentUserService currentUserService) : IRequestHandler<CreatePostCommand, Guid>
{
    private readonly IGenericRepository<Post, Guid> _postRepository = postRepository ?? throw new ArgumentNullException(nameof(postRepository));
    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    
    public async Task<Guid> Handle(CreatePostCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException();
        
        var post = new Post 
        { 
            Id = request.Id ?? Guid.NewGuid(),
            Title = request.Title, 
            Content = request.Content,
            AuthorId = userId,
            CategoryId = request.CategoryId
        };
        
        await _postRepository.AddAsync(post);
        return post.Id;
    }
}
```

### 4.2. Generic Repository & Specific Repository
Dự án áp dụng kết hợp cả hai mô hình Repository:
- **Generic Repository (`IGenericRepository<T, TKey>`)**: Sử dụng trực tiếp đối với các thực thể không có các yêu cầu truy vấn ghi phức tạp (như `Post`). Việc này loại bỏ hàng chục file repository rác chỉ chứa các hàm CRUD cơ bản.
- **Specific Repository (như `IProductRepository`)**: Sử dụng khi thực thể có các tác vụ ghi đặc thù hoặc truy vấn dữ liệu thô phục vụ nghiệp vụ ghi khó tối ưu bằng Generic (ví dụ: `SearchProductsAsync` trên `Product`).

**Mẫu triển khai Specific Repository (Chỉ viết khi thực sự cần thiết):**
```csharp
// Abstraction (Application/Interfaces/IProductRepository.cs)
namespace FloraCore.Application.Interfaces;

using FloraCore.Application.Common.Interfaces;
using FloraCore.Application.Features.Products.DTOs;
using FloraCore.Domain.Entities;

public interface IProductRepository : IGenericRepository<Product, Guid>
{
    Task<List<ProductSearchResultDto>> SearchProductsAsync(string searchTerm);
}

// Implementation (Infrastructure/Repositories/ProductRepository.cs)
namespace FloraCore.Infrastructure.Repositories;

public class ProductRepository(AppDbContext context) 
    : GenericRepository<Product, Guid>(context ?? throw new ArgumentNullException(nameof(context))), IProductRepository
{
    public async Task<List<ProductSearchResultDto>> SearchProductsAsync(string searchTerm)
    {
        return await _context.Products
            .Where(p => p.Name.Contains(searchTerm) || (p.Description != null && p.Description.Contains(searchTerm)))
            .Select(p => new ProductSearchResultDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Price = p.Price,
                Stock = p.Stock,
                ImageUrl = p.ImageUrl,
                AverageRating = p.AverageRating
            })
            .ToListAsync();
    }
}
```

### 4.3. Unit of Work định hướng Transaction
Khác với các triển khai truyền thống nơi `IUnitOfWork` chứa toàn bộ thuộc tính Repository dẫn đến tight coupling, `IUnitOfWork` trong FloraCore được tinh giản để **chỉ chịu trách nhiệm quản lý ranh giới Transaction**.

**Abstraction (`IUnitOfWork`):**
```csharp
namespace FloraCore.Application.Common.Interfaces;

public interface IUnitOfWork : IDisposable
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
```

---

## 5. Góc Nhìn Chuyên sâu & Lưu ý Kiến trúc (Deep Insights)

### 💡 Lưu ý 1: Tại sao không dùng Custom Repository cho mọi Entity?
Nhiều dự án Clean Architecture mắc sai lầm tạo ra hàng tá file `ICustomerRepository`, `CustomerRepository`, `IPostRepository`, `PostRepository` một cách máy móc. Điều này tạo ra lượng lớn boiler-plate code dư thừa vì tất cả đều chỉ gọi các hàm `Add`, `Update`, `Delete` cơ bản của EF Core.
- **Giải pháp của FloraCore**: Sử dụng trực tiếp `IGenericRepository<T, TKey>` cho luồng ghi của các entity thông thường. Chỉ tạo Repository riêng biệt khi cần viết logic ghi phức tạp mà EF Core DbContext thô không đáp ứng tốt hiệu năng hoặc nghiệp vụ đặc thù.

### 💡 Lưu ý 2: Tại sao luồng Query (Đọc) lại bỏ qua EF Core và đi thẳng tới Dapper?
EF Core là một công cụ ORM tuyệt vời cho các thao tác ghi (Commands) vì nó hỗ trợ Change Tracking, quản lý mối quan hệ thực thể phức tạp, bảo đảm tính nhất quán của dữ liệu. Tuy nhiên, đối với luồng truy vấn đọc (Queries):
1. **Performance**: Change Tracking của EF Core gây lãng phí bộ nhớ và CPU khi đọc lượng lớn bản ghi chỉ để hiển thị lên giao diện.
2. **Khả năng tối ưu SQL**: Các truy vấn đọc phức tạp (báo cáo, tìm kiếm nâng cao với nhiều điều kiện nối bảng) khi viết bằng LINQ thường sinh ra câu lệnh SQL rất dài, không tối ưu và khó kiểm soát.
- **Giải pháp của FloraCore**: Sử dụng Dapper trong triển khai `IPostQueryService`. Viết SQL thuần túy (hoặc Dynamic SQL) tối ưu trực tiếp cho cấu trúc DTO đầu ra. Luồng đọc hoàn toàn bypass qua lớp EF Core, tăng tốc độ phản hồi API đáng kể.

### 💡 Lưu ý 3: Tại sao IUnitOfWork không giữ tham chiếu Repository?
Nếu `IUnitOfWork` giữ tham chiếu đến tất cả Repositories (ví dụ: `IUnitOfWork.Posts.Add()`), thì mỗi lần thêm một thực thể mới vào hệ thống, ta lại phải sửa đổi cả interface và class triển khai `UnitOfWork` (vi phạm nguyên tắc Open/Closed).
- **Giải pháp của FloraCore**: Tách rời hoàn toàn. Repositories và Unit of Work dùng chung một phiên bản `AppDbContext` thông qua cơ chế Scoped Lifetime của Dependency Injection. Khi bạn gọi `AddAsync` trên repository và `SaveChangesAsync` trên Unit of Work, EF Core tự động liên kết chúng trong cùng một transaction.

---

## 6. Quy chuẩn C# 12+ & Coding Best Practices

### 6.1. C# 12 Primary Constructors
Bắt buộc áp dụng cấu trúc **Primary Constructors** của C# 12 đối với mọi lớp thực hiện Dependency Injection (Handlers, Services, Controllers).
- **Quy tắc Null Check**: Phải thực hiện kiểm tra `null` ngay tại dòng gán biến nội bộ bằng cú pháp `?? throw new ArgumentNullException(nameof(...))`.

```csharp
// ✅ ĐÚNG
public class ChatService(IHubContext<ChatHub, IChatClient> hubContext) : IChatService
{
    private readonly IHubContext<ChatHub, IChatClient> _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
}

// ❌ SAI (Cú pháp constructor cũ)
public class ChatService : IChatService
{
    private readonly IHubContext<ChatHub, IChatClient> _hubContext;
    
    public ChatService(IHubContext<ChatHub, IChatClient> hubContext)
    {
        _hubContext = hubContext;
    }
}
```

### 6.2. Sử dụng Records cho DTOs
Tất cả các Command đầu vào, Query tham số và DTO kết quả trả về bắt buộc định nghĩa bằng cú pháp **C# record**. Record hỗ trợ tính bất biến (immutability) và so sánh giá trị tích hợp, giảm thiểu lỗi runtime liên quan đến ghi đè dữ liệu.

```csharp
// ✅ ĐÚNG
public record PostDto(
    Guid Id,
    string Title,
    string AuthorName,
    DateTime CreatedAt
);
```

### 6.3. Async/Await Everywhere
Mọi tác vụ I/O (Database, Network, File) bắt buộc sử dụng cơ chế xử lý bất đồng bộ (`async/await`) kết hợp truyền nhận `CancellationToken` xuyên suốt từ Presentation xuống Infrastructure để tránh nghẽn luồng xử lý (thread starvation).

---

## 7. Chiến lược Kiểm thử (Testing Strategy)

Clean Architecture mang lại lợi thế cực lớn trong việc viết kiểm thử tự động nhờ phân tách ranh giới rõ ràng.

### 7.1. Unit Tests (Application Layer)
- **Mục tiêu**: Kiểm thử luồng xử lý nghiệp vụ của các Command/Query Handlers.
- **Phương pháp**: Mock toàn bộ các interface hạ tầng sử dụng thư viện `Moq`.

```csharp
public class CreatePostHandlerTests
{
    [Fact]
    public async Task Handle_ShouldCreatePost()
    {
        // Arrange
        var mockRepo = new Mock<IGenericRepository<Post, Guid>>();
        var mockUserService = new Mock<ICurrentUserService>();
        mockUserService.Setup(x => x.UserId).Returns(Guid.NewGuid());
        
        var handler = new CreatePostHandler(mockRepo.Object, mockUserService.Object);
        var command = new CreatePostCommand(null, "Title", "Content", null);
        
        // Act
        var result = await handler.Handle(command, CancellationToken.None);
        
        // Assert
        Assert.NotEqual(Guid.Empty, result);
        mockRepo.Verify(x => x.AddAsync(It.IsAny<Post>()), Times.Once);
    }
}
```

### 7.2. Domain Unit Tests
- **Mục tiêu**: Kiểm thử các quy tắc nghiệp vụ tự đóng gói trong các Entity và Value Object.
- **Phương pháp**: Không cần bất kỳ thư viện Mock nào vì Domain Layer hoàn toàn tinh khiết. Chỉ cần khởi tạo đối tượng bằng từ khóa `new` và thực hiện kiểm tra hành vi.

---

## 8. Các Lỗi Kiến trúc Thường gặp (Anti-Patterns)

### ❌ Lỗi 1: Tầng Application tham chiếu trực tiếp đến tầng Infrastructure
* **Dấu hiệu**: Trong file Command Handler của Application xuất hiện lệnh:
  `using FloraCore.Infrastructure.Repositories;` hoặc dùng concrete class `ProductRepository` thay vì `IProductRepository`.
* **Hậu quả**: Phá vỡ Dependency Rule. Tầng Application bị gắn chặt vào thư viện hoặc công nghệ cụ thể của tầng Infrastructure, mất khả năng thay đổi DB hoặc viết Unit Test độc lập.

### ❌ Lỗi 2: Trộn lẫn Validation dữ liệu đầu vào và nghiệp vụ hệ thống
* **Dấu hiệu**: Kiểm tra chiều dài ký tự của Title trong Command Handler hoặc kiểm tra dữ liệu email hợp lệ bằng logic tự viết thay vì sử dụng `FluentValidation` validator.
* **Hậu quả**: Code Handler bị phình to, lặp lại logic validation ở nhiều nơi.
* **Sửa lỗi**: Sử dụng validator class kế thừa `AbstractValidator<T>` đặt bên cạnh Command/Query. Hệ thống sẽ tự động bắt lỗi và dừng xử lý trước khi lệnh đi vào Handler nhờ bộ lọc `ValidationBehavior`.

### ❌ Lỗi 3: Đưa logic nghiệp vụ của Domain vào Controllers hoặc Handlers
* **Dấu hiệu**: Đoạn code thay đổi trạng thái entity xuất hiện tràn lan trong Controller hoặc Command Handler (như tính toán trung bình cộng Rating thủ công).
* **Hậu quả**: Anemic Domain Model. Domain layer chỉ chứa các class chứa thuộc tính get/set thụ động, nghiệp vụ bị phân tán và khó bảo trì.
* **Sửa lỗi**: Đóng gói logic thay đổi trạng thái và xác thực nghiệp vụ thành các phương thức bên trong Entity (như phương thức `AddRating(score)` đã trình bày ở mục 3.1).

---

> [!TIP]
> **Checklist trước khi tạo Pull Request (PR):**
> 1. Dự án lõi (Domain, Application) của bạn có tham chiếu đến thư viện Infrastructure hay Presentation nào không? (Phải là **KHÔNG**).
> 2. Các Service/Handler mới viết có áp dụng đầy đủ cú pháp C# 12 Primary Constructor kèm null check không? (Phải là **CÓ**).
> 3. Bạn đã viết kiểm thử tự động (Unit Test) cho luồng nghiệp vụ mới thêm chưa? (Phải là **CÓ**).
