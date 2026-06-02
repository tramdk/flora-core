# AGENTS.md — Quy trình & Quy tắc tối ưu cho Agent

## NGUYÊN TẮC VẬN HÀNH TỰ TRỊ (AUTONOMOUS EXECUTION)
- **Tự động chạy trọn gói (End-to-End)**: Khi nhận được 1 yêu cầu nghiệp vụ từ người dùng, Agent **BẮT BUỘC** phải tự động thực thi tuần tự và liên tục xuyên suốt qua cả 4 giai đoạn (Discovery -> Coding -> Verification -> Final Checks) ngay trong **một lượt phản hồi duy nhất** mà không được dừng lại giữa chừng để chờ người dùng chat thúc đẩy (như chờ xác nhận viết test, cấu hình, hay chạy check).
- **Chỉ dừng lại khi thực sự cần thiết**: Agent chỉ được phép tạm dừng và đợi phản hồi của người dùng trong hai trường hợp duy nhất: (1) Cần phê duyệt bản thiết kế lớn (Implementation Plan) trước khi bắt đầu viết code, hoặc (2) Gặp phải sự mơ hồ nghiêm trọng về mặt logic nghiệp vụ cốt lõi không thể tự quyết định trong code.
- **Báo cáo trọn vẹn**: Trả về kết quả hoàn chỉnh đã được build sạch sẽ, vượt qua toàn bộ unit test và đạt 100% static check ngay khi kết thúc lượt phản hồi đầu tiên.

## Giai đoạn 1: Khám phá & Định vị (Discovery)
1. **Đồng bộ & Ưu tiên CodeGraph**: 
   - LUÔN LUÔN chạy lệnh `codegraph sync` ở đầu mỗi phiên làm việc (hoặc sau khi pull code mới về) để đảm bảo bản đồ chỉ mục luôn phản ánh chính xác trạng thái hiện tại của codebase.
   - Luôn luôn sử dụng các tool của MCP Server `codegraph` đầu tiên để tìm kiếm symbol, truy vết mối quan hệ (references/calls) giữa các class/interface nhằm định vị nhanh tệp cần xử lý và tối ưu lượng token sử dụng.
2. **Xác định và tạo Kiểm thử trước (TDD)**:
   - Dựa vào kết quả từ CodeGraph, sử dụng công cụ đọc file để xem file test tương ứng trong `FloraCore.Tests/` trước khi viết code để hiểu rõ Method Signature và Expected Behavior mong muốn.
   - Khi phát triển tính năng mới hoặc sửa lỗi, **bắt buộc tạo/viết các kịch bản kiểm thử (Test Cases) mới trước khi viết logic**. Đảm bảo testcase mới được biên dịch và chạy thất bại trước (Red Light).
3. **Quét tài liệu Skill**: Quét danh sách `<skills>` có sẵn ở đầu phiên làm việc. Nếu phát hiện skill liên quan đến nghiệp vụ hiện tại (ví dụ: `optimizing-ef-core-queries`), bắt buộc đọc tệp `SKILL.md` tương ứng để áp dụng chính xác chỉ dẫn kỹ thuật.

## Giai đoạn 2: Thiết kế & Viết code (Coding & Design Rules)
4. **Quy trình TDD (Red - Green - Refactor) & Biên dịch ngay**:
   - **Red**: Chạy `dotnet test --filter <tên_test>` để xác nhận testcase mới tạo chạy thất bại.
   - **Green**: Chỉ viết hoặc sửa đổi 1 file production `.cs` tại một thời điểm với lượng code tối thiểu để testcase vượt qua thành công. Sau đó chạy ngay lệnh Build (`dotnet build FloraCore.csproj`) để kiểm tra lỗi cú pháp và chạy lại test để xác nhận trạng thái thành công (Green Light).
   - **Refactor**: Tiến hành tối ưu hóa cấu trúc code sau khi test đã thành công, đảm bảo các kiểm thử vẫn vượt qua sau khi tối ưu.
5. **Quy định Cốt lõi của Project**: Bắt buộc tuân thủ 100% các tiêu chuẩn thiết kế trong **`CODING_POLICY.md`**, tập trung vào:
   - *Phạm vi User Story*: Tuyệt đối không được tự ý thêm thắt các tính năng nằm ngoài phạm vi User Story.
   - *Độ bao phủ Test Case*: Bắt buộc 100% Test Case phải bao gồm cả 3 kịch bản: Happy Path, Edge Case (Dữ liệu biên), và Exception/Fail Path.
   - *Dependency Injection & Primary Constructors*: Sử dụng Primary Constructor của C# 12+ và null check.
   - *CQRS & MediatR*: Command/Query là record bất biến, Handler độc lập có XML comments.
   - *Phân trang & Lọc*: Hỗ trợ phân trang/bộ lọc, sử dụng AsNoTracking cho các truy vấn Read-only.
   - *Bất đồng bộ*: Dùng async/await cho các tác vụ I/O kèm CancellationToken.
   - *EF Core & Dapper Hybrid*: LINQ cho Commands, Dapper cho Read-only Queries hiệu năng cao.
   - *Quản lý Cấu hình & Lỗi*: Strongly-typed Configuration qua `IOptions<T>` và `IResourceManager` cho localization lỗi.
   - *Cấu trúc DTO & Feature Slices*: DTOs bắt buộc nằm trong thư mục `DTOs` chuyên dụng dưới mỗi Feature Slice.
   - *Bảo mật Secrets*: Tuyệt đối không hardcode API keys/secrets trong code hoặc JSON file; dùng biến môi trường `.env`.
   - *Quy tắc SSOT (Single Source of Truth)*: Đóng gói các quy tắc nghiệp vụ tập trung tại một nơi duy nhất.
   - *Spec-Driven Development*: Đồng bộ hóa API Contract qua `Specs/openapi.json` và test `ApiContractTests.cs`.

## Giai đoạn 3: Khắc phục lỗi & Kiểm thử (Verification & Troubleshooting)
6. **Xử lý lỗi Compiler**: Nếu gặp lỗi compiler (CS1503/CS1061), hãy đối chiếu interface hoặc class gốc. Nếu lỗi xuất hiện quá 3 lần, phải đọc kỹ file test tương ứng để lấy signature chuẩn xác.
7. **Phạm vi tác động của TestWriter**: Đối với vai trò TestWriter, CHỈ được chỉnh sửa các file nằm trong thư mục `FloraCore.Tests/`. Tuyệt đối KHÔNG chạm vào production code.
8. **Chạy thử nghiệm đơn vị**: Chạy kiểm thử toàn bộ dự án (`dotnet test`) hoặc filter cụ thể để xác nhận độ tin cậy.

## Giai đoạn 4: Nghiệm thu (Final Checks)
9. **Kiểm soát phiên bản**: KHÔNG gọi lệnh `git commit`. Chỉ sử dụng các lệnh chuẩn bị như `git status`, `git diff`, `git add`.
10. **Chạy Static Check**: LUÔN LUÔN chạy static check thông qua `./scripts/final-check.ps1 validate-all` đạt 100% trước khi hoàn tất.
11. **Đồng bộ hóa Chỉ mục Cuối phiên**: Luôn luôn chạy `codegraph sync` ngay trước khi bàn giao công việc.