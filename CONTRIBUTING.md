# Đóng góp cho Multi-Agent Hub

## Môi trường

Windows x64, .NET 10 SDK. Clone repository rồi chạy:

```powershell
dotnet build MultiAgentHub.slnx -c Release
dotnet run --project src-desktop -- --data-dir "$env:TEMP\MultiAgentHub-Dev"
```

Profile riêng tránh thay đổi cấu hình/hội thoại đang dùng. Không commit profile này hoặc API key. CLI/API do người phát triển tự cấu hình; project không yêu cầu một tài khoản hoặc cặp nhà cung cấp cụ thể.

## Cấu trúc

- `src-core/`: cấu hình, lưu trữ, adapter CLI/API, quota và điều phối.
- `src-desktop/`: WinForms, thư viện hội thoại, composer, thiết lập agent và bảng task.
- `tests-native/`: harness dùng CLI/HTTP giả lập, không cần tài khoản hoặc gọi model trả phí.
- `scripts/`: biên dịch, publish và đóng gói.
- `docs/`: hướng dẫn, kiến trúc và thiết kế ban đầu.

Xem [kiến trúc](docs/ARCHITECTURE.md) trước khi thay đổi engine. Provider/connection là lựa chọn trong Catalog; danh sách agent của một profile do người dùng tạo. Không thêm agent, model ID, tài khoản hoặc đường dẫn máy cá nhân vào cấu hình mặc định.

## Kết nối và tính năng mới

Trước hết cân nhắc executable CLI tùy chỉnh hoặc API tương thích đã có. Adapter native mới cần khai báo lựa chọn ở Catalog, ánh xạ invocation/parser hoặc API, metadata/model/login nếu công cụ hỗ trợ, và tài liệu giới hạn quyền. Điều phối dựa vào agent được chọn và khả năng kết nối; không kiểm tra ID của một agent cá nhân để quyết định workflow. Tách quy tắc provider khỏi giao diện.

Giữ tương thích cấu hình/hội thoại. Không tự nâng quyền, gửi lại inference hoặc mở đăng nhập khi khởi động. Quota phải đến từ lỗi rõ ràng, không từ nội dung câu trả lời thành công.

## Kiểm tra

```powershell
dotnet build MultiAgentHub.slnx -c Release
# Chỉ chạy khi muốn kiểm tra harness:
dotnet run --project tests-native -c Release
# Build/publish/ZIP, giữ app đang chạy:
./scripts/build-desktop.ps1 -PackageOnly -SkipTests
```

Bỏ `-SkipTests` để build script chạy harness trước khi đóng gói. Harness hiện có không chứng minh UI, toàn bộ luồng nhóm hoặc CLI live hoạt động. PR ghi rõ đã kiểm tra gì và chưa kiểm tra gì; không ghi test đạt khi chỉ compile. Không dùng tài khoản/endpoint trả phí trong kiểm thử tự động.

## Pull request

Mô tả vấn đề, hành vi sau sửa và cách kiểm chứng. Giữ thay đổi tập trung, cập nhật hướng dẫn khi hành vi thay đổi. Không đưa `app-bin`, `release`, `.build`, `bin`, `obj`, profile, log hoặc thông tin đăng nhập vào PR. Các lỗi chứa source riêng/tài khoản cần được rút gọn trước khi công khai.
