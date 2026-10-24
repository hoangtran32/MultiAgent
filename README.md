# Multi-Agent Hub

Ứng dụng Windows native để dùng một hoặc nhiều AI agent: trò chuyện độc lập hoặc cùng làm việc trong project.

**C# · .NET 10 · Code OSS · MIT** — Hub Code dùng editor VSCodium với extension Hub tích hợp; Hub Chat và quản lý tài khoản dùng WinForms. EXE chứa backend/runtime .NET, tải nền editor đã ghim checksum vào lần đầu mở Code; không cần cài Node.js riêng. Gateway HTTP loopback và browser bridge vẫn là các kết nối tùy chọn.

## Bắt đầu

**6.0.0 — Hub Code chuyển sang Code OSS:** file/editor nhiều tab, tìm kiếm, Git, terminal, debugger và extension của nền editor; khung Agent có phản hồi streaming, trạng thái, task, kế hoạch và kết quả. **Làm ngay** dùng một agent cho yêu cầu thông thường; **Kế hoạch → triển khai** giữ nhóm review và phân công Đầu não/Thợ code. Sửa vòng bàn luận lặp không tiến triển. [Cách dùng, cài lần đầu và phần chưa tương đương Antigravity](docs/CODE_OSS_IDE.md).

**5.15.0 — Hub Code / luồng IDE:** vào **Agent & model → Phân công code • Đầu não / Thợ code** để chọn kết nối và model riêng cho hai vai. Nhóm chốt từng ý, reviewer duyệt toàn bộ giải pháp rồi mới tự chuyển sang thiết kế thuật toán → triển khai → kiểm tra/review. Đổi phân công khi đang chạy được áp dụng ở ranh giới bước. Nút Workspace trên đầu chat có cây file/editor, Git diff, bảng task và nhật ký. Xem [luồng IDE](docs/CODE_IDE_WORKFLOW.md).

**5.14.0 — Harness dùng chung:** Chat/Code có runtime gọi công cụ thật cho API, quản lý ngữ cảnh, bộ nhớ riêng hội thoại, nhật ký có receipt và nhận bổ sung trong lúc chạy. API có thể đọc/tìm/sửa file trong Code theo quyền; Chat dùng tài liệu, web và artifact riêng. Vào **Agent & model → Runtime & công cụ**. Xem [thiết kế và cách dùng](docs/AGENT_HARNESS.md).

**5.13.0:** nhóm kết thúc khi đã đối chiếu và xác nhận đáp ứng yêu cầu; gợi ý tùy chọn không tự thành task. Khung chat hỗ trợ nhiều ảnh/PDF/Word/văn bản, kéo thả và Ctrl+V ảnh. Xem [điểm dừng và đính kèm](docs/COMPLETION_AND_ATTACHMENTS.md).

**API chạy ngầm dùng gói ChatGPT (5.12.0):** vào **Agent & model → Tài khoản ChatGPT • API**, đăng nhập chính thức và chọn model do tài khoản trả về. Sau đăng nhập không cần extension/tab/app mở. Model/tính năng API có giới hạn riêng; Gemini cùng quota app chưa có kết nối API được xác minh. Xem [hướng dẫn và giới hạn](docs/CHATGPT_PLAN_API.md).

Tải `MultiAgentHub-<version>-win-x64.exe` từ mục Releases của repository rồi mở trực tiếp. Hub Code tải nền VSCodium khoảng 229 MB lần đầu; các lần sau dùng bản đã lưu. Dùng `--native` cho Hub Chat, `--settings` để thiết lập tài khoản, `--code "đường-dẫn-project"` để mở IDE. Nếu dùng bản ZIP, giải nén toàn bộ rồi mở `MultiAgentHub.exe`.

1. Lần đầu app không có agent được cài sẵn. Bấm **Thêm agent của bạn** hoặc **Agent & model** để dò công cụ đã cài và model.
2. Chọn kết quả dò, cấu hình rồi lưu agent. Hoặc chọn công cụ AI, kiểu kết nối và số lượng để thêm thủ công. Kết quả dò không tự tạo/bật agent.
3. Mở **Thiết lập / model** để chọn model, executable/API endpoint, key và quyền. CLI dùng tài khoản đã đăng nhập trên máy; API key lưu trong Windows Credential Manager.
4. Bật **Chạy** cho agent đã cấu hình. Chọn **Trò chuyện** để hỏi đáp/nghiên cứu không cần project, hoặc **Làm việc** và chọn project để thực hiện task. Gửi bằng **Ctrl+Enter**.

Chỉ cần **một agent bất kỳ đã cấu hình** để chạy độc lập. Thêm đồng đội để cùng làm việc; tối đa 32 agent. Không bắt buộc có Codex, Antigravity, hoặc một cặp nhà cung cấp cụ thể. Có thể tạo nhiều agent dùng cùng công cụ với model/vai trò khác nhau. App không tự cài CLI, đăng nhập, gọi model hoặc nâng quyền khi khởi động.

## Trò chuyện nhóm

Trong **Hub Chat → Trò chuyện nhóm** (5.10.0), các chatbot là thành viên bình đẳng, tự nói với bạn và với nhau. Bạn đưa chủ đề/ý tưởng/vấn đề; bot trả lời từng ý, phản hồi đồng đội và cùng tìm giải pháp. Gửi “hello” thì các bot chào bạn và nhau; không ép thành bài nghiên cứu. Khi mọi bot đã sẵn sàng chờ bạn, trạng thái hiện **Chờ bạn**; tin nhắn mới tiếp tục ngay. Bạn có thể chen ý kiến khi nhóm đang trao đổi. Không cần project hoặc bấm Tiếp tục sau mỗi bot. Web extension chỉ là kênh gửi/nhận, không thay thế cơ chế hội thoại này.

## Gateway từ tài khoản web (5.11.0, thử nghiệm)

Vào **Agent & model → Gateway & tài khoản web** để quản lý tên tài khoản, liên kết phiên trình duyệt và chọn model được nhận diện. Không tạo sẵn agent/model. Đăng nhập vẫn trên trình duyệt để giữ đúng model/quota website, không thay bằng Codex OAuth hoặc Gemini Code Assist. Giữ tab mở; phiên này chưa phải đăng nhập độc lập hoàn toàn trong Hub.

Sau khi cài/cập nhật extension, mở chat web mới, chọn model, kết nối trong extension rồi gửi **một tin văn bản đầu tiên trên web**. Extension nhận diện request API trong document đó. Hub chỉ nhận model/metadata và văn bản, không nhận cookie/token đăng nhập. Sau đó các lượt **Gateway** gọi request API trực tiếp trong phiên trình duyệt, không gõ/bấm Gửi/đọc DOM. Cơ chế chat nhóm vẫn cho các bot chào bạn, phản hồi nhau và cùng giải quyết vấn đề.

Gateway cục bộ tùy chọn có `/v1/models` và `/v1/chat/completions` (văn bản, SSE), mặc định `http://127.0.0.1:20129/v1`, bật thủ công, bắt buộc key riêng của Hub. API key này chỉ bảo vệ endpoint cục bộ; không phải key tính phí của website. Xem [Gateway web](docs/WEB_GATEWAY.md) về thiết lập, giới hạn tương thích và xử lý lỗi. Adapter website là thử nghiệm: ChatGPT có thể yêu cầu handshake/xác minh chưa hỗ trợ; không coi request được nhận diện là đã kiểm chứng gọi thành công. Không tự đổi model, giải challenge hoặc gửi lại yêu cầu bị lỗi.

## Kết nối

### Chatbot từ extension, dùng tài khoản website đang có

Từ **5.9.0**, extension nhận diện website/model từ tab bạn liên kết; Hub không tạo sẵn cặp agent hay danh sách model. Vào **Agent & model → Chatbot từ trình duyệt…**, chuẩn bị bridge, cài Load unpacked, mở chat web mới và kết nối. Để nhãn trống để theo model đang chọn ở website. Một chatbot để hỏi đáp, nhiều chatbot để luân phiên nghiên cứu/phản biện trong Hub Chat, không cần project hay API key.

Extension đọc phản hồi fetch/XHR nhận diện được rồi parse văn bản/model về app, giữ DOM làm dự phòng. Không xuất phiên đăng nhập hoặc gọi lại endpoint nội bộ. Gemini/ChatGPT có adapter riêng, website HTTPS khác dùng bộ nhận diện chung thử nghiệm và có thể cần CSS; chưa kiểm thử live. Không bảo đảm nhận diện model website che giấu. Chi tiết: [Chatbot từ extension](docs/BROWSER_CHAT.md).

| Công cụ | Cách sử dụng |
|---|---|
| Claude / Anthropic | Claude Code CLI, Anthropic API hoặc sao chép/dán |
| Google Gemini | Gemini web qua extension, Gemini API hoặc sao chép/dán |
| ChatGPT chatbot | ChatGPT web qua extension hoặc sao chép/dán |
| Google Antigravity | CLI `agy`; IDE và app 2.0 qua sao chép/dán |
| Codex | Codex CLI hoặc sao chép/dán |
| API tương thích OpenAI / local AI | Chat Completions endpoint, model và key của người dùng |
| Công cụ khác | Executable/script CLI với đối số cấu hình, hoặc sao chép/dán |

Danh sách trên là các adapter có sẵn để lựa chọn. CLI tùy chỉnh và API tương thích dùng được mà không cần sửa source. API thuần văn bản chưa có công cụ tự đọc/sửa filesystem hoặc chạy terminal. IDE/app thủ công cần dán kết quả về Hub và không tham gia phiên bàn luận/nhóm tự động.

**Dò công cụ & model** tìm Codex/Claude/Antigravity CLI đã cài và các kết nối API/CLI tùy chỉnh đã cấu hình. Model có nguồn rõ ràng: `agy models`, cache Codex, cấu hình Claude hoặc API `/models`. Cache/cấu hình cục bộ không xác nhận model còn được cấp quyền; công cụ chưa cung cấp danh sách thì dùng mặc định CLI hoặc nhập ID. Không gợi ý ID model cố định, không gọi inference khi dò. Bộ chọn kết nối vẫn dùng được trong lúc tải model.

## Cách làm việc

- **Hub Code / Làm việc** dùng project và giữ nguyên quyền CLI đã chọn. **Hub Chat / Trò chuyện** lưu hội thoại độc lập, không cần folder, không gắn đường dẫn project cũ. Lịch sử/sidebar và lựa chọn agent tách theo từng không gian.
- Chatbot extension mặc định dùng cho Trò chuyện. Có thể chủ động chọn trong Làm việc để tư vấn, đề xuất code và phản biện qua văn bản; CLI hoặc người dùng áp dụng thay đổi. Không tự chuyển sang chatbot web hay gửi dữ liệu workspace khi CLI hết quota.
- **Chốt từng ý**: một ý nhỏ → phản biện/sửa đến khi cùng chốt phiên bản → thực hiện/review → ý kế tiếp. [Hướng dẫn](docs/CONSENSUS_USER_GUIDE.md).
- **Song song**: cùng nhận yêu cầu. **Lần lượt / phản biện**: kết quả trước chuyển cho agent sau. **Cùng làm việc**: thực hiện → rà soát → hoàn thiện.
- **Bàn luận qua lại**: các model phát triển, phản biện ý tưởng theo giới hạn lượt/thời gian, có bản tổng hợp cuối.
- **Bàn tròn & thực hiện**: trao đổi ngang hàng, đề xuất task có phụ thuộc/tiêu chí, thực hiện tuần tự và review. PM, Architect, Coder, Tester, Reviewer là chuyên môn, không phải năm tài khoản bắt buộc.
- Thư viện project và hội thoại hỗ trợ tiếp tục, tìm kiếm, ghim, đổi tên, lưu trữ và bản TXT tự cập nhật.
- Nhấp phải project để ghim, đổi tên hiển thị, chuyển/tạo mục, mở Explorer, lưu trữ hội thoại hoặc gỡ khỏi thư viện. Gỡ giữ nguyên thư mục và dữ liệu chat; thêm lại cùng đường dẫn để khôi phục. Chuyển chat đọc file ở nền và giữ cache các chat gần nhất cùng vị trí cuộn.
- Tin nhắn của bạn có bong bóng xanh nhạt bên phải; các AI có khung riêng bên trái, đường phân cách nhẹ và nút Sao chép/Chi tiết. Chỉ tin đang thay đổi được cập nhật khi streaming.
- Hết quota rõ ràng thì bỏ chọn agent đó và tiếp tục với kết nối còn khả dụng; còn một thì chạy đơn. Không tự bật lại hoặc gửi lại khi mở app.

**Toàn quyền** cho phép CLI dùng file/terminal theo yêu cầu. Người dùng chọn quyền phù hợp cho từng kết nối. Antigravity chạy ngầm có thể từ chối lệnh cần phê duyệt khi chưa có allow-rule hoặc toàn quyền; xem hướng dẫn chi tiết. Trong luồng nhóm, mặc định người dùng tự test; kết quả không có bằng chứng kiểm tra được ghi **Chưa kiểm chứng**.

## Tài liệu

- [Hướng dẫn sử dụng đầy đủ](docs/USER_GUIDE.md)
- [Nhóm AI, task và kiểm chứng](docs/TEAM_USER_GUIDE.md)
- [Kiến trúc và giới hạn hiện tại](docs/ARCHITECTURE.md)
- [Đóng góp và phát triển](CONTRIBUTING.md)
- [Thay đổi phiên bản](CHANGELOG.md)
- [Bảo mật và dữ liệu cá nhân](SECURITY.md)

## Build từ source

Cần Windows x64 và .NET 10 SDK. Không có NuGet dependencies bên ngoài.

```powershell
dotnet build MultiAgentHub.slnx -c Release
# Build một EXE có runtime để tải lên GitHub Releases, không test chức năng:
./scripts/build-release.ps1 -SkipTests
# Chỉ biên dịch/đóng gói, không test chức năng, không thay app đang chạy:
./scripts/build-desktop.ps1 -PackageOnly -SkipTests
# Cài bản build vào app-bin và tạo shortcut (đóng Hub trước):
./scripts/build-desktop.ps1 -Zip -CreateShortcut -SkipTests
```

EXE nằm tại `release/MultiAgentHub-<version>-win-x64.exe`, kèm file SHA-256; ZIP nằm tại `release/MultiAgentHub-win-x64.zip`. `Open_App.bat` mở bản cài trong `app-bin`; `build-desktop.bat` build, chạy harness và tạo shortcut. Các build script mặc định chạy harness nếu không có `-SkipTests`. GitHub Actions chạy harness và đóng gói trên Windows; kiểm tra này không thay thế kiểm thử UI/live agent. Bản 5.8.2 hiện chỉ được kiểm tra biên dịch/đóng gói cục bộ, chưa chạy test chức năng/màn hình hoặc đo hiệu năng chuyển chat.

## Dữ liệu và Git

Cấu hình/hội thoại nằm ngoài repo tại `%LOCALAPPDATA%\MultiAgentHub`; API key riêng nằm trong Windows Credential Manager. ZIP chỉ chứa app, tài liệu và giấy phép, không kèm tài khoản hoặc agent của người build. Cấu hình cũ vẫn được giữ khi cập nhật; profile mới bắt đầu trống.

`.gitignore` loại build/cache, cấu hình cá nhân, lịch sử, key và file môi trường. Push source và tài liệu; đưa ZIP lên GitHub Releases thay vì commit binary. Dữ liệu task đang chạy không được thay đổi bởi chế độ `-PackageOnly`.

MIT License. Bản Windows đi kèm giấy phép .NET và thông báo thành phần bên thứ ba trong `licenses/`.
