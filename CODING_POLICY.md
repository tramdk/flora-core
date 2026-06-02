# CODING POLICY & AGENT INSTRUCTIONS
> **BẮT BUỘC:** Mọi AI Agent và lập trình viên phải đọc, ký cam kết ngầm và tuân thủ 100% các quy tắc trong tài liệu này trước khi thực thi bất kỳ thay đổi nào trong kho lưu trữ.

---

## 🛡️ 1. Nguyên Tắc Cốt Lõi (Core Principles)
*   **Clean Code & SOLID:** Code phải tự giải thích (self-explanatory), phương thức ngắn gọn tập trung vào một nhiệm vụ (Single Responsibility).
*   **Surgical Changes (Thay đổi phẫu thuật):** Chỉ sửa đổi chính xác những dòng code cần thiết. Không tự ý định dạng lại hoặc tái cấu trúc (refactor) code xung quanh đang chạy ổn định.
*   **Phạm vi tối giản (Strict Scope):** Tuyệt đối không được tự ý thêm thắt các tính năng nằm ngoài phạm vi User Story.
*   **Goal-Driven (Định hướng mục tiêu):** Xác định tiêu chí thành công rõ ràng trước khi code. Chạy kiểm thử tự động lặp lại liên tục cho đến khi thành công.
*   **An Toàn & Bảo Mật:** Tuyệt đối không hardcode bí mật (secrets). Bất kỳ lỗi bảo mật nào đều không được chấp nhận.

---

## 💻 2. Quy Tắc Cho Backend (.NET 9 C# - FloraCore)

### A. Kiến Trúc & Cấu Trúc Thư Mục
Dự án áp dụng chặt chẽ **Clean Architecture + CQRS Pattern**. Hướng phụ thuộc: **Domain <- Application <- Infrastructure <- Controllers/API**.
*   **Tên Namespace:** Phải tuân theo cấu trúc phân cấp: `FloraCore.{Tầng}.{Tính Năng}` (Ví dụ: `FloraCore.Application.Products.Queries`).
*   **Cấu trúc chuẩn của một Feature Slice (Vertical Slice Directory Structure):**
    Mỗi thư mục tính năng nằm trong `Application/Features/{Tính Năng}/` bắt buộc phải phân chia cấu trúc phân cấp nghiêm ngặt như sau (nếu có):
    *   `Commands/`: Chứa các Command và Command Handler xử lý ghi (Write logic).
    *   `Queries/`: Chứa các Query và Query Handler xử lý đọc (Read-only logic).
    *   `DTOs/`: Chứa toàn bộ các Data Transfer Objects (DTO) phục vụ riêng cho tính năng này. **Tuyệt đối không** đặt trực tiếp DTO trong thư mục `Queries` hoặc `Commands`.
    *   *Tên Namespace của DTO*: Phải tuân theo định dạng `FloraCore.Application.Features.{Tính Năng}.DTOs` (Ví dụ: `FloraCore.Application.Features.Products.DTOs`).
*   **Controllers Rất Mỏng (Thin Controllers):** Controllers chỉ nhận request, ủy quyền xử lý hoàn toàn cho tầng Application thông qua MediatR, và trả về kết quả. Không chứa logic nghiệp vụ.
*   **SOLID & Code Quality:**
    *   Đảm bảo tuân thủ nghiêm ngặt các nguyên lý SOLID (Single Responsibility, Open/Closed, Liskov Substitution, Interface Segregation, Dependency Inversion).
    *   Sử dụng interface segregation với quy ước đặt tên rõ ràng (tiền tố 'I' cho mọi interface).
    *   Giữ cho các phương thức ngắn gọn, tập trung và có tính gắn kết (cohesive) cao. Tránh trùng lặp mã bằng cách sử dụng các lớp cơ sở (base classes) và utilities.

### B. Thiết Kế Các Design Patterns Bắt Buộc
*   **Command Pattern (CQRS):**
    *   Mỗi Command/Query phải là một `record` bất biến (immutable) kế thừa `IRequest<T>`.
    *   Triển khai bộ xử lý thông qua Handler độc lập kế thừa `IRequestHandler<TRequest, TResponse>`.
    *   Bắt buộc viết XML comments đầy đủ cho Command/Query và Handler.
    *   *Mở rộng*: Triển khai Command Handler pattern với các lớp cơ sở generic (ví dụ: `CommandHandler<TOptions>`) hoặc interface tương ứng nếu cần tùy biến tham số hoặc options.
*   **Factory Pattern:**
    *   Sử dụng Factory Pattern để tạo ra các đối tượng phức tạp có tích hợp cơ chế Service Provider (DI container).
*   **Repository Pattern:**
    *   Tác vụ database phải đi qua interface repository trừu tượng hóa để đảm bảo khả năng unit test và mock.
    *   Interfaces repository đặt tại `Application/Interfaces/`, implementation đặt tại `Infrastructure/Repositories/`.
*   **Provider Pattern:**
    *   Sử dụng để trừu tượng hóa các dịch vụ bên ngoài (cơ sở dữ liệu, dịch vụ AI), định nghĩa hợp đồng (contract) rõ ràng và xử lý cấu hình tập trung.
*   **Dependency Injection & Primary Constructors:**
    *   **Bắt buộc** sử dụng cú pháp **Primary Constructor** của C# 12+ cho DI (ví dụ: `public class ProductRepository(AppDbContext context)`).
    *   Phải thực hiện kiểm tra null ngay trong constructor bằng `ArgumentNullException.ThrowIfNull(dependency)`.
    *   Đăng ký service với Lifetime chuẩn xác: `Scoped` cho DbContext/Repository, `Transient` cho logic xử lý nhẹ, `Singleton` cho cache/app state.
*   **Resource Pattern (ResourceManager):**
    *   Tuyệt đối không viết cứng chuỗi thông báo lỗi hoặc log trong code.
    *   Phải sử dụng `ResourceManager` để đọc chuỗi từ các tệp `.resx` được phân nhóm rõ ràng (ví dụ: `LogMessages.resx`, `ErrorMessages.resx`).
    *   Truy xuất qua cú pháp: `_resourceManager.GetString("ProductNotFound")`.

### C. Cơ Chế EF Core & Dapper Hybrid (Yêu cầu đặc biệt của FloraCore)
*   **EF Core:** Sử dụng LINQ & EF Core cho các thao tác thêm, sửa, xóa (Commands) và các truy vấn thực thể có cập nhật trạng thái. Cấu hình quan hệ Entities bằng Fluent API trong phương thức `OnModelCreating` của DbContext.
*   **Dapper:** Sử dụng Dapper cho các truy vấn đọc hiệu năng cao (Read-only Queries, Tìm kiếm phức tạp, Thống kê) để tăng tốc độ phản hồi API, hạn chế tối đa Over-fetching.
*   **Tối ưu hóa EF Core (Lỗi N+1):** Luôn sử dụng `.Include()` chủ động để tải dữ liệu liên kết (Eager Loading), hoặc dùng `.Select()` để chỉ lấy đúng các trường dữ liệu cần thiết từ Database, tránh memory bloat.

### D. Cấu Hình & DI nâng cao (`IOptions<T>`)
*   **Strongly-typed Configuration:** Định nghĩa class cấu hình tương ứng với các phân đoạn trong `appsettings.json`.
*   **Tuyệt đối không** inject trực tiếp interface `IConfiguration` rải rác trong các Repository hoặc Services.
*   **Bắt buộc** sử dụng mô hình Strongly-typed Configuration: Định nghĩa class cấu hình tương ứng với json settings, kiểm chứng bằng Data Annotations (`[Required]`, `[MinLength]`, `[NotEmptyOrWhitespace]`), và inject thông qua **`IOptions<T>`** hoặc **`IOptionsSnapshot<T>`** (khi cần reload động cấu hình cấu trúc settings).

### E. Tài Liệu XML (XML Documentation)
*   **Bắt buộc** viết tài liệu XML comments (sử dụng dấu `///`) cho **TẤT CẢ** các public classes, interfaces, methods và properties.
*   Ghi rõ mô tả cho từng tham số (`<param name="...">`) và giá trị trả về (`<returns>`).

### F. Asynchronous & Modern C#
*   Luôn dùng `async/await` cho các tác vụ I/O (Database, Network, File). Hàm async phải có hậu tố `Async`.
*   **Tuyệt đối không** dùng `.Result` hoặc `.Wait()` để tránh gây Deadlock. Hãy luôn truyền `CancellationToken`.
*   Trả về `Task` hoặc `Task<T>` từ các phương thức bất đồng bộ. Sử dụng `ConfigureAwait(false)` ở các tầng thư viện độc lập không phụ thuộc vào context đồng bộ.
*   Sử dụng **File-Scoped Namespaces** để giảm mức độ thụt lề thụ động.
*   Xử lý triệt để cảnh báo Nullable Reference Types (`CS8618`, `CS8620`...).
*   Sử dụng tính năng C# 12+ và tối ưu hóa .NET 9 mới nhất khi khả thi.

### G. Quản Lý Lỗi & Ghi Log (Error Handling & Logging)
*   Sử dụng ghi log có cấu trúc (Structured Logging) với `Microsoft.Extensions.Logging`.
*   Bao gồm scoped logging với ngữ cảnh có ý nghĩa để dễ dàng truy vết sự cố.
*   Ném các exception cụ thể kèm theo thông điệp rõ ràng, tránh ném các Exception chung chung.
*   Sử dụng khối `try-catch` một cách hợp lý đối với các kịch bản lỗi có thể dự đoán trước.

### H. Tích Hợp AI & Semantic Kernel (Nếu áp dụng)
*   Sử dụng `Microsoft.SemanticKernel` cho các tác vụ tích hợp AI.
*   Cấu hình kernel và đăng ký service đúng chuẩn DI.
*   Quản lý cấu hình AI model (ChatCompletion, Embedding) tập trung.
*   Sử dụng các mẫu thiết kế Output có cấu trúc (Structured Output Patterns) để đảm bảo phản hồi tin cậy từ AI.

---


## 3. Workflows Của Agent Khi Nhận Task Mới
Mỗi khi nhận một yêu cầu phát triển tính năng mới, AI Agent phải tuân theo lộ trình sau:
1.  **Phân tích (Analyze):** Đọc các file liên quan để hiểu ngữ cảnh. (VD: Nếu yêu cầu thêm field mới cho `Product`, phải xem xét cả `Product.cs`, `AppDbContext.cs`, DTOs,...).
2.  **Lên kế hoạch (Plan):** Đưa ra một tóm tắt ngắn để người dùng biết sẽ sửa những file nào.
3.  **Thực thi (Execute):** Viết code hoặc sửa code một cách chi tiết.
4.  **Kiểm tra (Verify):** Khuyến khích sử dụng các lệnh Terminal (`dotnet build` cho hệ Backend) để đảm bảo không có lỗi cú pháp.

## 4. Thiết Kế API (RESTful & GraphQL)
*   **Routing & Naming:** Luôn dùng danh từ số nhiều cho RESTful API endpoints (VD: `/api/products` thay vì `/api/getProduct`).
*   **HTTP Methods:** Phân định rõ ràng: `GET` (đọc), `POST` (tạo mới), `PUT/PATCH` (cập nhật), `DELETE` (xóa).
*   **Standard Response Format:**
    *   Với dữ liệu thành công: Có thể trả về trực tiếp Data hoặc dùng một wrapper thống nhất (ví dụ: `ApiResponse<T> { data, message, isSuccess }`) tùy theo convention của team Frontend.
    *   Với dữ liệu lỗi: Bắt buộc tuân thủ chuẩn **RFC 7807 (`ProblemDetails`)** vốn là chuẩn mặc định của .NET Core hiện tại, tránh việc tự chế format lỗi lộn xộn.
*   **Phân trang & Lọc:** Các API trả về danh sách lớn (`GET /api/products`) MẶC ĐỊNH phải có tham số phân trang (`pageIndex`, `pageSize`).


## 🛡️ 5. Quy Tắc Bảo Mật & Xác Thực (Security & Authentication)

### A. Secrets Management
*   **Tuyệt đối không** commit API keys, connection strings, mật khẩu hoặc secrets lên Git.
*   Mọi thông tin nhạy cảm phải được lưu trong biến môi trường (`.env` cho Frontend, `appsettings.json` / User Secrets cho Backend).
*   Harness và App phải tự động kiểm tra sự tồn tại của các biến môi trường cấu hình bắt buộc khi khởi động và báo lỗi sớm nếu thiếu.

### B. Phòng Chống SQL Injection
*   **Bắt buộc sử dụng parameterized queries** (truy vấn có tham số) thông qua EF Core hoặc Dapper.
*   **Tuyệt đối không** dùng phép cộng chuỗi (`+` hoặc `${}`) để ghép dữ liệu người dùng trực tiếp vào câu lệnh SQL thô.

### C. Input Validation (Kiểm Tra Đầu Vào)
*   Validate dữ liệu ở cả hai đầu:
    *   *Frontend*: Dùng các schema validator như `Zod` để kiểm tra định dạng và báo lỗi sớm cho người dùng.
    *   *Backend*: Dùng `FluentValidation` để validate chặt chẽ trước khi Handler xử lý dữ liệu.
*   Với file upload: Kiểm tra nghiêm ngặt dung lượng tối đa (ví dụ: 5MB), phần mở rộng file hợp lệ (whitelist extension) và định dạng MIME thực tế.

### D. XSS & CSRF Prevention
*   Sử dụng cơ chế escape tự động của React. Nếu bắt buộc render HTML thô qua `dangerouslySetInnerHTML`, phải khử trùng dữ liệu trước bằng thư viện **`DOMPurify`**.
*   JWT Tokens phải được lưu trữ trong **httpOnly Cookies** (không lưu trong `localStorage` để tránh bị tấn công XSS đánh cắp session). Cấu hình thuộc tính cookie: `Secure; SameSite=Strict`.
*   Kích hoạt CSRF tokens cho tất cả các API thay đổi trạng thái (POST, PUT, DELETE).

### E. Quản Lý Log & Lỗi An Toàn
*   **Tuyệt đối không** ghi thông tin nhạy cảm (mật khẩu, mã PIN, số thẻ tín dụng, JWT tokens) vào tập tin Log.
*   Trong môi trường Production, **không bao giờ** trả về stack trace (vết lỗi hệ thống) chi tiết hoặc lỗi DB thô cho Client. Chỉ trả về mã lỗi chung thân thiện hoặc ProblemDetails chuẩn **RFC 7807**, đồng thời ghi vết chi tiết vào log server bảo mật.

---

## 🧪 5. Tiêu Chuẩn Kiểm Thử (Testing Standards)
*   **Quy tắc 3 Kịch bản Bắt buộc:** Bắt buộc 100% Test Case phải bao gồm cả 3 kịch bản: Happy Path, Edge Case (Dữ liệu biên), và Exception/Fail Path.
*   **Unit & Integration Tests:** Mọi logic nghiệp vụ trong Application layer phải có Unit Test bao phủ đầy đủ cả kịch bản thành công và thất bại.
*   **Khung kiểm thử:** Sử dụng khung kiểm thử xUnit kết hợp cùng thư viện `FluentAssertions` cho các biểu thức khẳng định (assertions).
*   **AAA Pattern:** Từng test case bắt buộc phân chia 3 phân đoạn rõ ràng bằng comment: `// Arrange`, `// Act`, `// Assert`.
*   **Mocks:** Sử dụng `Moq` (hoặc NSubstitute) để tạo dữ liệu giả lập cho các dependency bên ngoài khi viết Unit Tests cho Handlers.
*   **Null Parameter Validation Tests:** Bắt buộc viết các test case kiểm tra tính hợp lệ của tham số null đối với các Service/Repository.

---

## 🚀 7. Quy Trình Vận Hành & Quản Lý Dự Án GSD (Get Shit Done)
Dự án áp dụng phương pháp quản lý và lập kế hoạch GSD tối giản, tập trung hiệu năng cho lập trình viên solo và AI Agent:

### A. Lập Kế Hoạch Đi Lùi (Goal-Backward Planning)
*   Trước khi viết code cho bất kỳ tính năng nào, **tuyệt đối không** hỏi "chúng ta phải code những gì?". Thay vào đó, hãy xác định: **"Mục tiêu cuối cùng là gì? Điều gì bắt buộc phải ĐÚNG để mục tiêu đó thành công?"**
*   *Ví dụ thay vì lập danh sách*: "Tạo nút tìm kiếm" $\rightarrow$ *Hãy định nghĩa*: "Người dùng có thể nhập ký tự tìm kiếm và nhận đúng danh sách sản phẩm khớp tên, bất kể viết hoa viết thường."
*   Từ định nghĩa "điều phải đúng", suy ngược ra danh sách các file cần viết và test case cần phủ.

### B. Kế Hoạch Là Prompt (Plans are Prompts)
*   Mọi tệp kế hoạch trong thư mục `.planning/` hoặc tài liệu mô tả yêu cầu đều được thiết kế như một cấu trúc chỉ thị (Executable Prompts) để Agent đọc và thi hành trực tiếp, loại bỏ các thủ tục báo cáo rườm rã.

### C. Ngân Sách Ngữ Cảnh (Context Budgeting)
*   AI Agent và Harness phải liên tục giám sát lượng tiêu thụ ngữ cảnh (token). Khi phiên làm việc quá dài và lượng sử dụng ngữ cảnh tiệm cận mức **50%**, Agent phải tự giác dừng lại, hoàn tất commit nhỏ và báo cáo người dùng để bàn giao ngữ cảnh mới sạch sẽ, tránh suy giảm chất lượng lập trình (Code degradation).

### D. Atomic Commits (Commit Độc Lập)
*   Mỗi tác vụ nhỏ được hoàn thành trong một chặng (như viết xong interface repository, hoặc viết xong Unit test) phải được commit độc lập với mã định danh rõ ràng, giữ cho lịch sử Git cực kỳ sạch sẽ và dễ dàng rollback khi phát hiện lỗi.
    *   *Ví dụ*: `feat(01-01): implement IProductRepository interface`, `test(01-02): add unit tests for product search`.

---
*Ghi chú: Việc commit hoặc sửa đổi mã nguồn mà vi phạm bất kỳ quy tắc nào trong CODING_POLICY.md này sẽ bị bộ lọc kiểm duyệt của Harness (GAN Evaluator) đánh trượt điểm và tự động hoàn tác (rollback).*