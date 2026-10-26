# Changelog

## 6.1.0 — 2026-10-26

- Thay launcher VSCodium bằng sản phẩm Hub Code build từ source editor ghim commit; tên/icon, executable, protocol, ID Windows và profile riêng. Không sửa bản VSCodium người dùng đã cài.
- EXE standalone chứa editor, Agent tích hợp sẵn và backend; cài từ bundle có checksum, không tải nền editor bên ngoài. Giữ LICENSE/ThirdPartyNotices của source và dependency.
- Agent mở được ở Restricted Mode, hiển thị hướng dẫn Trust trước khi chạy backend/terminal. Mở trực tiếp executable editor vẫn có Agent/backend; không phụ thuộc biến môi trường từ launcher.
- Workflow build source riêng và fixture kiểm tra checksum, đúng sản phẩm/Agent, executable bị sửa và ZIP traversal. Build native/debug không có editor dùng cửa sổ Hub hiện có.

## 6.0.0 — 2026-10-24

- Chuyển Hub Code sang Code OSS qua VSCodium Windows x64, tải nền editor chính thức và xác minh SHA-256 trước khi cài. EXE tự chứa extension Hub và backend; giữ Hub Chat/thiết lập native.
- Extension Agent bên editor: streaming, trạng thái thực tế, task/kế hoạch/walkthrough, Git changes, terminal, nhật ký, tệp đính kèm và ngữ cảnh code đang chọn. Lịch sử/checkpoint riêng theo project, nhập các phiên work cũ đúng project; không nhập tài khoản cá nhân vào source/release.
- Làm ngay mặc định gửi đúng một agent/model rồi kết thúc; Kế hoạch giữ review toàn bộ trước code, phân công Đầu não/Thợ code riêng theo project, đổi vai tại ranh giới bước. Review code dùng quyền chỉ đọc.
- Dừng các vòng bàn luận lặp không thay đổi công việc; chủ động review kế hoạch đã ổn định. Không dùng giới hạn gọi/thời gian để giả hoàn thành. Câu hỏi không cần task có đường đối chiếu kết thúc.
- JSON stdio không mở cổng mới; model metadata không chặn lệnh Dừng, gửi lỗi/timeout không tự replay, khóa writer chung và khôi phục sau ngắt. Kiểm tra CI offline cho session, framing, vòng lặp, khóa workspace và backend EXE headless. Chưa kiểm tra UI/live model, chưa đạt tương đương 100% Antigravity; chi tiết trong CODE_OSS_IDE.md.

## 5.15.0 — 2026-10-24

- Thêm luồng IDE tùy chọn cho Hub Code / Chốt từng ý: chốt kế hoạch từng ý, toàn nhóm review giải pháp đầy đủ trước khi mở bước code. Kế hoạch planned không bị coi là đã triển khai.
- Phân công riêng Đầu não thuật toán và Thợ code, mỗi vai chọn agent + model độc lập với vai thảo luận. Đầu não bàn giao implementationPlan theo đúng task/version; Thợ code mới thực hiện, rồi qua kiểm tra và peer review. Không gắn sẵn nhà cung cấp/model hoặc đổi vai ngầm khi lỗi.
- Đổi hai vai code trong phiên đang chạy: xếp hàng, áp dụng tại ranh giới bước, loại bản thiết kế cũ đang chờ nếu phân công đổi; giữ thao tác đã bắt đầu/task đã xong. Bổ sung phạm vi làm hết hiệu lực review kế hoạch trước khi code tiếp.
- Workspace native có cây file/editor văn bản, lưu với hash chống ghi đè bản mới, giữ BOM/encoding, Git diff chỉ đọc và nhật ký agent/lệnh. Bản nháp không bị bỏ khi đổi project; tạm khóa lưu khi agent chạy.
- Hiển thị giai đoạn review giải pháp, đầu não thiết kế và thợ code triển khai; bảng task/bản ghi có model thực tế và bàn giao thuật toán.
- Fixture offline CI kiểm tra thứ tự review → code, bất đồng, đổi vai trong lúc thiết kế, race yêu cầu mới, lỗi đầu não, resume giữ task đã xong và editor/encoding/hash. Build cục bộ; chưa chạy suy luận/UI live.

## 5.14.0 — 2026-10-24

- Thêm harness native dùng chung cho Hub Chat/Code: model → function calls → thực thi công cụ → trả kết quả về cùng ngữ cảnh → câu trả lời cuối. Nối Chat Completions, Gemini, Anthropic và ChatGPT plan Responses; giữ opaque reasoning/thought signatures và cặp call/result đúng protocol. CLI/extension/gateway giữ transport hiện có.
- Công cụ đọc/tìm workspace, ghi file có SHA256 chống ghi đè thay đổi cũ; executable + mảng đối số trong project khi quyền full. Lập kế hoạch/review/sửa schema qua API không có công cụ ghi/lệnh. Chat có tệp, bộ nhớ theo hội thoại, artifact văn bản và đọc URL HTTPS công khai; tìm kiếm dùng URL endpoint do người dùng cấu hình.
- Nhật ký ghi pending trước thao tác, kết quả sau thao tác; giữ receipt bị gián đoạn và không tự replay. Context giữ nguyên yêu cầu, rút nhóm công cụ cũ nguyên cặp với đường dẫn đọc lại. Nhận chỉ dẫn mới ở ranh giới bước; bỏ đề xuất công cụ cũ nếu có bổ sung trong lúc model chạy.
- Thêm Runtime & công cụ trong Agent & model, quyền API trong editor; hiển thị hoạt động công cụ và số lần gọi model/công cụ. Giới hạn chỉnh được, 0 lượt = không giới hạn; ngân sách nhóm tính cả lần gọi bên trong harness.
- Thêm fixture offline cho quyền, ghi có hash, traversal, công cụ thật trên fixture, cancellation/receipt, steering, memory isolation, context, provider payload và JobRunner Chat/Code. Chỉ build tại máy; kiểm tra chức năng bằng CI, chưa suy luận/UI live.

## 5.13.0 — 2026-10-23

- Bỏ yêu cầu mỗi lượt phải phát triển ý mới/bàn giao. Giữ phạm vi theo yêu cầu trực tiếp của người dùng; gợi ý tùy chọn không thành task hay điều kiện nghiệm thu.
- Chat/Bàn luận kết thúc khi các thành viên xác nhận complete trên cùng phiên bản yêu cầu, không gọi thêm lượt tổng hợp. Tin mới vô hiệu hóa xác nhận cũ; lỗi/quota không tính là đồng thuận. Footer thiếu lặp lại ở phiên không giới hạn dừng rõ là lỗi điều phối, không gọi vô hạn.
- Nhóm/Chốt từng ý có bước đối chiếu điểm dừng khi task đã chốt xong: không tạo task trong bước này; phần bắt buộc còn thiếu phải dẫn nguyên văn yêu cầu người dùng. Bất đồng chưa giải quyết dừng chưa kiểm chứng. Không tự mở lại task đã xong chỉ vì một tin nhắn bổ sung.
- Đính kèm nhiều ảnh/PDF/Word .docx/văn bản qua nút +, kéo thả, Ctrl+V; danh sách có bỏ từng tệp. Bản sao riêng có kiểm tra hash khi mở lại, metadata hiện trong hội thoại, không đưa base64 vào lịch sử JSON.
- API ChatGPT plan/Chat Completions/Gemini/Anthropic gửi ảnh và PDF gốc khi model hỗ trợ. Kênh CLI/extension nhận chữ trích từ PDF/Word, chặn ảnh/PDF quét trước khi gửi. Không đổi kết nối, billing hay tệp nguồn. Giới hạn 10 tệp/20 MB, 10 MB mỗi tệp, 120.000 ký tự trích xuất; chưa OCR hoặc trích hình Word.
- Fixture offline mới cho điểm dừng, tin nhắn mới trong lượt, ngăn task ngoài phạm vi, parser tài liệu, payload tệp và bản lưu. Không chạy suy luận/kiểm tra UI trên tài khoản thật tại máy.

## 5.12.0 — 2026-10-21

- Thêm kết nối API chính thức dùng gói ChatGPT: OAuth public client/PKCE, callback loopback, xác minh ID token bằng JWKS; đăng nhập tài khoản/workspace riêng cho Hub, không đọc credential app Codex hay trình duyệt.
- Model lấy động từ `/v1/models` của tài khoản. Đọc Responses SSE trực tiếp, chỉ hoàn tất khi nhận `response.completed`; giữ văn bản khi lỗi/luồng bị ngắt, không tự gửi lại, đổi model hay chuyển sang key tính phí.
- Token mã hóa Windows DPAPI, cập nhật nguyên bộ, khóa refresh theo tài khoản giữa các tiến trình; quản lý đăng nhập/đăng xuất và liên kết ChatGPT Settings → Usage. Hiện Using ChatGPT plan cạnh composer.
- Chưa hỗ trợ API dùng đúng quota/model Gemini desktop; không coi đường dẫn EXE là endpoint hay thay ngầm bằng CLI/API key. Kết nối extension/gateway thử nghiệm cũ giữ riêng.
- Bổ sung fixture offline JWT, SSE, DPAPI và khóa token trong CI. Không chạy đăng nhập/inference thật hoặc kiểm tra UI cục bộ.

## 5.11.1 — 2026-10-21

- Sửa kênh tab chatbot làm rơi câu trả lời khi prompt nhóm bị web thu gọn: đối chiếu phần đầu đủ dài với đúng một tin mới, composer đã gửi/rỗng và dấu hiệu thu gọn; giữ nonce document, bằng chứng request và chặn thao tác gửi/chuyển chat xen vào.
- Xác nhận URL mới theo hai bước: background cập nhật route trước, content chỉ phát phản hồi sau commit; tránh mất kết quả về ngay trong lúc SPA gán URL cuộc trò chuyện.
- Nhận kết quả từ tin AI mới có nút sao chép và đã hết trạng thái tạo, kể cả trả lời nhanh đến mức không bắt được spinner. Bổ sung nhãn Copy/Sao chép của Gemini. Không dùng im lặng văn bản để giả hoàn thành.
- Extension 1.3.1; fixture offline chạy chính content script cho prompt thu gọn, trả lời nhanh, hai website và chuyển chat thủ công, kèm fixture race URL/response. Không chạy UI/inference tại máy người dùng.

## 5.11.0 — 2026-10-21

- Thêm Gateway & tài khoản web: registry tên tài khoản/liên kết document, model từ request thật; không tạo agent/model mặc định, không thay model/quota web bằng OAuth CLI. Đăng nhập/xác minh giữ trong trình duyệt, tab cần mở.
- Extension 1.3.0 nhận diện API từ một request văn bản đầu tiên trong chat mới được liên kết. Lượt gateway dùng fetch trong phiên web và parser luồng phản hồi; không điều khiển composer/đọc DOM. Template/header giữ trong bộ nhớ document, không chuyển credential sang Hub. Adapter thử nghiệm Gemini RPC, ChatGPT web và cùng origin Chat Completions; handshake/challenge chưa hỗ trợ báo lỗi rõ, không tự replay hoặc đổi model.
- HTTP gateway loopback tùy chọn: /v1/models, /v1/chat/completions văn bản/SSE, key cục bộ trong Windows Credential Manager, kiểm tra Host/Origin, giới hạn body; từ chối tham số tools/multimodal/generation chưa hỗ trợ.
- Sửa mất phản hồi do URL SPA được cấp trước tin người dùng trong DOM. Request có bằng chứng prompt tương ứng được dùng để nối URL/luồng; bổ sung parser ChatGPT snapshot/patch và nhận diện Gemini RPC EOF có frame đầy đủ. Vẫn ngắt khi document/chat khác không được xác nhận.
- Build/cú pháp cục bộ; fixture giao thức/gateway/IPC chạy trong CI, không chạy inference hay kiểm tra màn hình trên máy người dùng.

## 5.10.0 — 2026-10-21

- Hub Chat / Trò chuyện nhóm mô phỏng cuộc nói chuyện giữa người dùng và các chatbot bình đẳng: biết tên đồng đội, chào hỏi, trả lời đúng người, từng ý ngắn, tự cùng tìm giải pháp. Bỏ ép báo cáo/sổ ý tưởng/bàn giao ở mỗi lượt chat; Hub Code giữ workflow nghiên cứu hiện có.
- Người dùng đưa ý tưởng/vấn đề và có thể chen vào bất kỳ lúc nào. Chatbot tự nối lượt, có thể mời đúng đồng đội đáp; mỗi thành viên được cơ hội phát biểu. Lịch sử trộn user/AI theo thứ tự, giữ ngữ cảnh gần nhất có giới hạn.
- Tách trạng thái đang trao đổi và Chờ bạn. Chỉ nghỉ khi mọi chatbot còn kết nối đã phát biểu và đều sẵn sàng chờ; tin nhắn mới đánh thức ngay, không đợi timer và không hủy câu đang trả lời. Không tự tổng hợp chỉ vì một bot đã nói xong. Metadata thiếu/sai không coi là nhóm đồng thuận hoặc hoàn tất task.
- Dòng điều phối nội bộ có nonce riêng mỗi lượt, ẩn khỏi text/stream người dùng; lưu metadata riêng để phục hồi và chẩn đoán. Thêm fixture offline cho chào hỏi hai bot, phản hồi chéo, chen lời, fairness và tín hiệu nghỉ. Chỉ build/cú pháp cục bộ; không gọi model thật hoặc test màn hình.

## 5.9.0 — 2026-10-20

- Kết nối Chatbot từ extension chung; website/model do tab đã liên kết báo về, không gán trước hai agent hoặc model mặc định. Giữ kết nối Gemini/ChatGPT cũ tương thích; website HTTPS khác dùng adapter chung thử nghiệm, có thể cần CSS.
- Observer fetch/XHR theo từng lượt trong tab, correlate đúng prompt POST cùng origin, parse response/stream thành văn bản/model/trạng thái. Không xuất credential, không replay endpoint, không thêm quyền đọc tất cả website; DOM dự phòng cho giao thức/đường truyền chưa nhận diện.
- Model quan sát từ phản hồi, nhãn giao diện và nhãn riêng hiển thị nguồn riêng. Nhãn riêng để trống để theo website; model bị che ghi rõ chưa nhận diện. Metadata thay đổi không ngắt lượt.
- Giữ kiểm tra document/URL và chống gửi lại; chỉ chốt bằng tín hiệu hoàn tất rõ, không coi EOF/đáp án bị cắt là thành công. Bổ sung fixture parser/observer/generic binding trong CI. Không chạy test chức năng/màn hình hoặc gọi model thật cục bộ.

## 5.8.2 — 2026-10-20

- Sửa extension ngắt tab ngay khi Chrome báo loading. Kiểm tra nonce/document và URL trước khi kết luận tải lại; loading/complete trong cùng document giữ binding và request đang chạy.
- Xử lý sự kiện loading kèm URL của cuộc chat mới qua xác nhận own-submit hiện có; tuần tự hóa các sự kiện của từng tab để tránh trạng thái cũ ghi đè URL mới.
- Tải lại thật, đổi chat không được xác nhận hoặc tab bị discard vẫn ngắt, giữ phần trả lời và không replay prompt. Lỗi phân biệt document mất, đổi trang và tab ngủ.
- Thêm hồi quy offline cho Gemini/ChatGPT, SPA và reload/discard thật trong CI; chỉ build/kiểm tra cú pháp trên máy, chưa kiểm thử website live.

## 5.8.1 — 2026-10-20

- Giữ ID khi kết nối lại cùng tab, document và model; không làm agent Hub gắn mã cũ chỉ vì bấm Kết nối lần nữa.
- Extension tự kiểm tra tab đã liên kết và nối lại cầu nối khi mở popup sau khi Hub khởi động lại; thêm Đồng bộ lại với Hub. Không yêu cầu chat mới cho document cũ còn hợp lệ, không gửi lại prompt hoặc đổi tab khi mất kết nối.
- Phân biệt tab đã liên kết với trạng thái cầu nối Hub. Preflight mở đúng thiết lập agent nếu mã tab/model cũ, giữ nguyên yêu cầu, project và conversation; không tự chọn tab khác. Lưu agent browser kiểm tra liên kết hiện tại.
- Build/cú pháp cục bộ; kiểm tra protocol đồng bộ giả lập và native qua CI. Không chạy test màn hình hoặc gửi yêu cầu thật trên máy.

## 5.8.0 — 2026-10-20

- Tách Trò chuyện khỏi workspace: không kiểm tra/bắt chọn thư mục, không đưa path project cũ vào prompt hoặc bản ghi chat. CLI dùng một thư mục runtime riêng khi cần; project/quyền của Làm việc được giữ nguyên.
- Sidebar Trò chuyện chỉ liệt kê hội thoại, ghim và lưu trữ; ẩn dự án/nút thêm project và dòng path trong composer. Chuyển không gian mở hội thoại mới, không nối nhầm vào task đang xem ở mục kia.
- Lịch sử ghi không gian riêng; chat cũ được nhận diện từ job và tách khỏi project khi đọc, không xóa hoặc viết lại nội dung cũ. Tiếp tục chat không phụ thuộc thư mục đã đổi/xóa. Đính kèm chat nhận file văn bản do người dùng chọn ở bất kỳ thư mục nào.
- Cho phép chủ động chọn chatbot extension để hỗ trợ công việc bằng văn bản, gồm tư vấn/code mẫu/phản biện. Không giả là chatbot có filesystem/terminal; không tự chuyển dữ liệu workspace sang web khi CLI hết quota.
- Biên dịch cục bộ; hồi quy chạy trong CI, không kiểm tra màn hình hoặc gửi yêu cầu AI thật trên máy.

## 5.7.1 — 2026-10-20

- Extension tự quét ô soạn chat khi mở, loại ô tìm kiếm/ô ẩn/ô khóa, ưu tiên vùng composer và dò lại nút gửi khi có nội dung. Không yêu cầu chỉ một phần tử khớp CSS cũ.
- Đọc nhãn model từ điều khiển trên trang nếu nhận diện chắc, vẫn cần người dùng xác nhận model thực tế. Thêm Tự quét lại và chọn trực tiếp ô nhập trên trang khi có nhiều vùng soạn; không điền/gửi thử hoặc thay bản nháp khi quét.
- Giữ CSS nâng cao, kiểm tra tín hiệu hoàn tất và chống gửi lại khi lỗi. Cần Reload extension và tải lại tab để thay script đã được tiêm ở bản trước.
- Kiểm tra cú pháp/biên dịch cục bộ; quy tắc nhận diện được kiểm tra offline trong CI. Chưa kiểm thử UI/live website.

## 5.7.0 — 2026-10-20

- Bridge trình duyệt thử nghiệm cho Gemini/ChatGPT dùng tài khoản web, không qua API key/agy. Extension nhúng vào single EXE; người dùng chủ động cài/nối tab. Lựa chọn Hub Chat riêng với agent Hub Code.
- Model do người dùng chọn/xác nhận trên website. Chủ đề Hub mới không dùng tiếp webchat thuộc chủ đề khác; kết nối mới yêu cầu chat web trống.
- Native Messaging C# + named pipe CurrentUserOnly, handshake và framing có giới hạn, không tự replay/fallback. Chỉ chốt lượt khi có tin mới và tín hiệu hoàn tất UI; giữ partial khi không nhận diện chắc.
- Bàn luận hỗ trợ 0 = không giới hạn lượt/thời gian/timeout, mặc định liên tục, tổng hợp chủ đề và sổ ý tưởng. Giữ link nguồn trong câu trả lời web. Cài đặt mới không bị lịch sử cũ ghi đè.
- Build/cú pháp cục bộ, harness qua CI; chưa test UI/live website hoặc suy luận thật. Hướng dẫn tại docs/BROWSER_CHAT.md.

## 5.6.0 — 2026-10-20

- Thêm Trao đổi liên tục cho Chốt từng ý/Bàn tròn: 0 là không giới hạn số lần gọi, thời gian phiên hoặc thời gian mỗi lượt. Bỏ timeout cứng 10 phút review/30 phút thực hiện; timeout từng lượt mặc định tắt.
- Đưa Trao đổi & giới hạn thành tab đầu trong cài đặt nhóm; số lần gọi, thời gian và phân vai/model có mục riêng, nội dung cuộn được.
- Mở hội thoại cũ giữ cài đặt nhóm hiện tại cho lần tiếp tục, không khôi phục giới hạn 30 cũ từ lịch sử. Hiển thị rõ không giới hạn thay vì bộ đếm /0.
- Không giới hạn không bỏ qua review, lỗi/quota, vướng mắc thực tế hoặc thao tác Dừng. Mặc định 30 lần/120 phút ở profile mới vẫn sửa/tắt được.
- Chỉ biên dịch/đóng gói cục bộ; không chạy kiểm thử chức năng/màn hình cục bộ. Harness chạy trên GitHub CI.

## 5.5.2 — 2026-10-17

- Dò thêm PATH người dùng/hệ thống hiện tại khi app đang mở; mở rộng biến môi trường và bỏ dấu ngoặc kép ở đường dẫn executable đã cấu hình.
- Lỗi không tìm thấy executable ghi tên file/đường dẫn và adapter thực tế, tách khỏi tên hiển thị tùy chỉnh; không quy lỗi đăng nhập hoặc quota khi chưa chạy CLI.
- Giữ nguyên yêu cầu khi preflight thiếu CLI và mở đúng thiết lập agent để sửa. Không đổi IDE/dán kết quả thành CLI, không tự đổi installation khi đường dẫn đã chỉ định bị mất.
- Biên dịch/đóng gói cục bộ; không chạy kiểm thử chức năng hoặc màn hình cục bộ.

## 5.5.1 — 2026-10-16

- Chuyển tiến độ vào panel gọn bên trái, dưới danh sách project/chat. Bỏ thanh trải ngang và progress bar native; giữ trạng thái, task, agent, số đã review, thời gian và thao tác. Chiều cao cố định theo nội dung, không dùng FlowLayout kéo giãn. Nút Dừng trong sidebar vẫn dùng được khi điều hướng/cấu hình đang khóa.
- Thêm loading ba chấm ở cuối hội thoại với tên agent đang xử lý, kể cả chưa có chữ mới. Animation chỉ vẽ lại indicator, không dựng lại tin nhắn hoặc đổi scroll; dừng khi phiên kết thúc, mở lịch sử hoặc feed bị ẩn. Chờ kết quả thủ công không giả là AI đang chạy.
- Không lặp toàn bộ Directives tích lũy ở cuối mỗi lượt tiếp tục. Yêu cầu đã có trong prompt/lịch sử không bị nhân đôi; bổ sung mới lưu thành tin nhắn theo thứ tự queued/đã áp dụng. Bổ sung legacy chưa có tin nhắn vẫn được giữ trước phần trả lời của phiên, dữ liệu gốc/export không mất.
- Bổ sung kiểm tra hồi quy cho loading theo trạng thái thực, lọc phần bổ sung kế thừa và vòng đời tin nhắn bổ sung của nhóm. Chỉ biên dịch/đóng gói cục bộ; không chạy test UI/chức năng cục bộ theo yêu cầu.

## 5.5.0 — 2026-10-16

- Thêm thanh trạng thái cố định trên ô nhập: phân biệt đang làm, đang chốt ý, phản biện, kiểm tra, chờ dán kết quả, nghỉ giữa lượt, lưu kết quả, hoàn tất, chưa kiểm chứng và dừng khi chưa xong.
- Hiển thị agent/task đang xử lý, số task đã review, trạng thái từng agent, thời gian và nguyên nhân dừng. Giới hạn gọi model được ghi riêng, không dùng làm tiến độ công việc; thanh tiến độ chỉ tính task/agent đã xong trong danh sách hiện có. Mở lịch sử không giả là đang chạy.
- Đưa nút Dừng có chữ sang thanh trạng thái, bỏ ô vuông khó nhận biết trong composer. Có Công việc kèm số đã xong, Chi tiết và Tiếp tục cho phiên dang dở; bản nháp đang gõ được giữ để người dùng tự gửi. Phiên mới ẩn thanh trạng thái.
- Bảng task dùng trạng thái dễ hiểu, tô màu, ưu tiên chọn phần đang làm khi chưa chọn và cập nhật tại chỗ để giữ lựa chọn, giảm dựng lại bảng khi nhận snapshot.
- Thêm 7 ca hồi quy về ý nghĩa tiến độ và phân biệt trạng thái. Chỉ biên dịch/đóng gói cục bộ, không chạy test UI/chức năng cục bộ theo yêu cầu.

## 5.4.3 — 2026-10-16

- CLI tách sự kiện kết thúc process gốc, EOF của pipe và sự kiện hoàn tất model. Codex turn.completed, Claude result thành công và Antigravity result SUCCESS có nội dung cho phép nhận kết quả mà không chờ CLI mắc ở bước thoát. Tin nhắn giữa lượt, đầu ra rỗng và result lỗi không phải hoàn tất.
- Sau tín hiệu hoàn tất, cho CLI thời gian thoát ngắn; nếu vẫn kẹt chỉ dừng process CLI gốc, giữ tiến trình nền của project. Thu stdout/stderr có giới hạn, không chờ pipe do process con giữ mở. Kết quả vẫn phải qua kiểm tra định dạng và peer review.
- Timeout từng lượt được phân biệt với người dùng hủy phiên; thông báo nêu agent, bước và giới hạn 30/10 phút. Giữ đầu ra trước timeout; khi tiếp tục đưa phần này vào ngữ cảnh với nhãn chưa xác nhận, yêu cầu đối chiếu file/bằng chứng trước khi làm lại.
- Thêm 5 ca hồi quy về tín hiệu terminal, CLI treo sau hoàn tất, giữ process con, nối AgentExecutor, không nhận partial khi hủy và timeout/tiếp tục. Chỉ biên dịch/đóng gói cục bộ, không chạy test màn hình/chức năng cục bộ theo yêu cầu.

## 5.4.2 — 2026-10-16

- Yêu cầu mới trực tiếp từ người dùng được ưu tiên hơn giới hạn trong kế hoạch/model report cũ. Giữ phần bổ sung mới nhất khi rút gọn prompt, không để lịch sử dài làm mất chỉ dẫn vừa gửi.
- Tiếp tục ý bị chặn đưa ý đó về bàn tròn, bắt buộc cập nhật đầy đủ phạm vi trước khi bỏ phiếu và thực hiện. Hướng xử lý sau kiểm chứng blocked cũng cần cập nhật phương án, không chỉ bỏ phiếu lại mô tả cũ. Giữ nguyên các task đã hoàn tất khi tiếp tục.
- Thẻ vướng mắc phân biệt đang kiểm chứng, đang cập nhật phạm vi và kết luận đã dừng; lý do phiên trước được ghi rõ là lịch sử. Không báo đã nghiệm thu chỉ vì người dùng cho phép thử lại.
- Bổ sung ca hồi quy về quyền mới thay phạm vi cũ, giữ task đã xong, sửa phạm vi trước quorum và chỉ dẫn đến trong lúc kiểm chứng. Chỉ biên dịch/đóng gói cục bộ; không chạy kiểm tra màn hình hoặc chức năng cục bộ theo yêu cầu.

## 5.4.1 — 2026-10-16

- Chốt từng ý không dừng ngay khi agent thực hiện báo blocked: chuyển vướng mắc cho đồng đội kiểm chứng. Có hướng xử lý trong phạm vi thì mở lại đúng ý, tăng phiên bản, xóa xác nhận cũ và chốt lại trước khi thử. Không mở ý khác hoặc coi bị chặn là hoàn thành.
- Đồng đội xác nhận cần điều kiện bên ngoài phải ghi lý do và thao tác/điều kiện cần để tiếp tục. Chat có thẻ vướng mắc riêng; trạng thái, bảng task và TXT/Markdown nêu rõ lý do, không chỉ ghi ý chưa xong. Không tự đổi tiến trình/cấu hình ngoài phạm vi để bỏ qua điều kiện nghiệm thu.
- Tiếp tục phiên bị chặn sẽ kiểm chứng vướng mắc trước khi gọi lại agent thực hiện; tương thích dữ liệu blocked cũ. Hướng xử lý mới vẫn bị giới hạn bởi số vòng sửa, số lần gọi và thời gian.
- Thêm 5 ca hồi quy cho kiểm chứng blocked, tìm cách xử lý và chốt lại, điều kiện bên ngoài, giới hạn thử lại và tiếp tục phiên blocked. Chỉ biên dịch/đóng gói cục bộ, không chạy UI/test chức năng cục bộ theo yêu cầu.

## 5.4.0 — 2026-10-16

- Thêm Chốt từng ý: chỉ một ý mở, phản hồi ngắn 100–200 từ; mọi thành viên khả dụng phải xác nhận cùng phiên bản trước khi thực hiện và review phần đó rồi mở ý tiếp. Cùng làm, Bàn luận và Bàn tròn vẫn giữ cách phối hợp riêng.
- Phương án sửa tăng phiên bản và xóa xác nhận cũ; xác nhận sai phiên bản, mở ý tiếp quá sớm hoặc tuyên bố kết thúc khi còn ý mở bị từ chối. Không dừng sau ba vòng phản biện; tiếp tục trong ngân sách đã đặt, hết giới hạn lưu ý chưa chốt.
- Hub Chat/chỉ lập kế hoạch chốt từng ý thành kế hoạch, không chạy bước thực hiện. Bổ sung yêu cầu hoặc tiếp tục ý chưa xong cần xác nhận lại. Agent lỗi thường giữ phiên dang dở thay vì loại bên phản biện để giả đồng thuận; quota cho phép chạy đơn, ghi rõ thiếu đồng thuận/review độc lập.
- Bảng Công việc & đầu ra có phiên bản và số thành viên đã chốt; JSON/TXT/Markdown lưu xác nhận. Quản lý nhóm dùng chung tại Agent & model → Phân vai & giới hạn nhóm. Checkpoint và nhận bổ sung cùng khóa snapshot.
- Thêm 8 ca hồi quy về phiên bản, quorum nhiều agent, phản đối, nhiều ý liên tiếp, lỗi, quota, chỉ lập kế hoạch và tiếp tục. Chỉ biên dịch/đóng gói cục bộ; không chạy test UI hoặc chức năng cục bộ theo yêu cầu.

## 5.3.4 — 2026-10-16

- Bàn luận mặc định chạy liên tục (nghỉ 0 giây), tự chuyển model/lượt sau khi có kết quả; không cần người dùng gửi bổ sung. Mẫu 8 giờ cũng không chèn nghỉ. Giới hạn lượt/thời gian và quota vẫn có hiệu lực.
- Ô nghỉ được ghi rõ là tùy chọn, 0 = liên tục. Khi mở/dùng lại hội thoại cũ, giữ nhịp chạy hiện tại thay vì khôi phục khoảng nghỉ 600 giây từ lịch sử; số lượt/thời gian cũ vẫn khôi phục.
- Các luồng CLI/API Song song, Lần lượt, Cùng làm và Bàn tròn không chèn thời gian nghỉ giữa các bước. Kết nối dán kết quả thủ công vẫn cần người dùng cung cấp kết quả.
- Ca hồi quy luân phiên model dùng cấu hình nghỉ mặc định và timeout để phát hiện việc chờ tin nhắn/đợi 600 giây trở lại. Chỉ biên dịch/đóng gói cục bộ, không chạy test màn hình hoặc chức năng theo yêu cầu.

## 5.3.3 — 2026-10-15

- Bàn luận vẫn nhập/gửi bổ sung được khi model đang chạy hoặc nghỉ giữa lượt. Gửi bổ sung đánh thức thời gian nghỉ, áp dụng từ lượt tiếp theo, không hủy câu trả lời đang chạy; giới hạn 12.000 ký tự/bổ sung và 100 bổ sung/phiên.
- Menu ⋯ có Chạy lượt tiếp ngay và Kết thúc & tổng hợp. Kết thúc sớm đợi lượt hiện tại rồi tổng hợp trong ngân sách đã đặt; nút Dừng vẫn hủy ngay. Khi đang tổng hợp, giữ ô nhập để soạn bản nháp, gửi tiếp khi phiên đã kết thúc.
- Tin bổ sung có ID riêng, nằm bên phải, lưu JSON/TXT/Markdown và đưa vào ngữ cảnh khi tiếp tục. Chỉ lượt model tính vào số lượt; ghi rõ bổ sung đang chờ hay đã gửi vào lượt nào. Checkpoint bàn luận và nhận bổ sung dùng chung khóa để tránh snapshot cũ ghi đè tin vừa nhận.
- Trạng thái hiện thời gian nghỉ còn lại và cách gửi để tiếp tục ngay; cài đặt giải thích hoàn tất một lượt khác kết thúc cả phiên. Không tự bật lại Codex hoặc agent hết quota.
- Thêm 3 ca hồi quy cho gửi khi nghỉ, bỏ nghỉ/kết thúc sớm và giữ kết quả đang chạy khi tổng hợp. Chỉ biên dịch/đóng gói cục bộ; không chạy test chức năng hoặc màn hình theo yêu cầu.

## 5.3.2 — 2026-10-15

- Hiển thị đường dẫn thư mục project ngay trên ô nhập, trong khung chat. Cập nhật theo folder đã chọn, chat mới và hội thoại cũ; tên hiển thị riêng của project không che mất đường dẫn thật.
- Đường dẫn dài thu gọn bằng dấu …, rê chuột để xem đầy đủ. Hàng đường dẫn có chiều cao riêng theo DPI, không nằm trong nội dung nhập hoặc mất khi gõ yêu cầu.
- Chỉ biên dịch/đóng gói cục bộ; không chạy kiểm tra màn hình hoặc test chức năng theo yêu cầu.

## 5.3.1 — 2026-10-15

- Giữ một nơi quản lý Agent & model ở cuối sidebar; bỏ nút thiết lập trong ô chat, thanh icon ngoài cùng và lối tắt quản lý trùng trong các menu. Bộ chọn agent cạnh nút gửi chỉ bật/tắt thành viên.
- Chuyển phân vai/giới hạn bàn tròn và giới hạn bàn luận vào màn hình Agent & model; vẫn cấu hình được cả hai trước khi chọn chế độ. Thay/bỏ file qua nút +; dừng sau bước hiện tại qua menu ⋯ khi bàn tròn đang chạy.
- Rê chuột trên nút cách phối hợp hoặc từng mục menu để đọc giải thích riêng cho Song song, Lần lượt, Cùng làm, Bàn luận và Bàn tròn, kèm ví dụ và nơi chỉnh giới hạn.
- Chỉ biên dịch/đóng gói cục bộ; không chạy kiểm tra màn hình hoặc test chức năng theo yêu cầu.

## 5.3.0 — 2026-10-15

- Menu chuột phải project: Ghim/Bỏ ghim, Chỉnh sửa tên hiển thị, Mục (chọn/tạo mục), Mở trong File Explorer, Lưu trữ/khôi phục các cuộc trò chuyện và Gỡ dự án. Có icon và đường phân cách; menu dùng được qua Shift+F10/phím Menu.
- Tên, ghim, mục và dấu gỡ project lưu riêng trong cấu hình Hub. Gỡ không xóa thư mục, job JSON hay TXT; project không tự xuất hiện lại từ lịch sử. Thêm lại cùng đường dẫn khôi phục tổ chức cũ. Lưu trữ hàng loạt chỉ đổi metadata của đúng project.
- Chuyển hội thoại đọc file ở nền, hủy yêu cầu cũ khi chọn chat khác và bỏ kết quả trễ. Cache tối đa 4 bộ dữ liệu theo thời gian sửa metadata; giữ 3 vùng chat native cùng vị trí đọc để chuyển lại không dựng lại RichEdit.
- Không xóa/dựng lại sidebar hay màn hình chào khi chỉ chuyển chat khác project. Nhật ký/câu trả lời riêng chỉ dựng khi mở Chi tiết; không cập nhật control chi tiết ẩn theo mỗi snapshot.
- Tải 32 tin gần nhất trước, bấm Hiển thị tin nhắn cũ để tải thêm; dữ liệu hội thoại/TXT vẫn đầy đủ. Chưa kiểm tra màn hình/chức năng hoặc đo hiệu năng thực tế cục bộ.

## 5.2.0 — 2026-10-15

- Thêm Dò công cụ & model: tìm CLI thực sự đã cài và liệt kê kết nối API đã cấu hình. Mở lần đầu quản lý agent trống sẽ dẫn vào màn hình dò; kết quả chỉ là lựa chọn, không tự tạo hay bật agent.
- Model lấy từ `agy models`, cache Codex, cấu hình Claude hoặc API `/models`; ghi rõ nguồn và giới hạn xác minh. Bỏ gợi ý model Claude cố định. Không dùng inference để dò model.
- Thiết lập agent tự dò executable/model khi mở hoặc đổi kết nối. Đổi kết nối hủy truy vấn cũ; bộ chọn và nút lưu vẫn sử dụng được khi dò, giữ model người dùng đang nhập.
- Khi chọn provider, ưu tiên CLI đã phát hiện thay vì mặc định chọn IDE/thủ công. API/CLI tùy chỉnh và cách chọn executable trực tiếp vẫn được hỗ trợ.

## 5.1.11 — 2026-10-15

- Hoàn tất layout và cập nhật giới hạn cuộn native trước khi khôi phục điểm đọc. Tin mới/streaming không còn nằm ngoài giới hạn cuộn cũ; tính vị trí cuối theo viewport thực tế.
- Lăn chuột trên phần nền/khung/thao tác tin nhắn đi vào cùng vùng cuộn hội thoại. Gửi tin hoặc bổ sung yêu cầu chủ động chuyển về cuối; khi cuộn lên đọc, các cập nhật tiếp theo giữ điểm đọc.
- Thêm script build EXE Windows x64 duy nhất có runtime, tên theo phiên bản và SHA-256 để tải lên GitHub Releases.

## 5.1.10 — 2026-10-15

- Ô nhập xử lý đo chiều cao sau khi native TextBox nhận xong phím, gộp yêu cầu layout và chỉ cập nhật hàng bố cục khi chiều cao thực sự đổi. Bỏ gán chiều cao trực tiếp cho panel đang dock, giữ hàng toolbar/đính kèm trong vùng của chúng; yêu cầu dài cuộn bên trong ô nhập.
- Khi nhóm đang chạy, cập nhật streaming không còn tắt rồi bật lại ô nhập/nút gửi bổ sung, tránh làm mất focus bàn phím giữa lúc gõ.
- Chỉ xóa ô nhập sau khi gửi yêu cầu thành công; cập nhật/kết thúc phiên không tự xóa bản nháp trùng với yêu cầu trước đó.

## 5.1.9 — 2026-10-14

- Bỏ bảng lồng có hàng tự đo chiều cao ở header sidebar. Nút Hub Code/Chat và tìm kiếm được đặt trong cùng viewport, lấy kích thước thực của header và căn giữa theo DPI để tránh cắt chữ/đáy nút.

## 5.1.8 — 2026-10-14

- Tin người dùng nằm trong bong bóng xanh nhạt bên phải; mỗi lượt AI có khung riêng, đường phân cách metadata/nội dung/thao tác và nút Sao chép/Chi tiết. Menu nhấp phải hỗ trợ sao chép đoạn chọn hoặc toàn bộ tin.
- Khung tin nhắn giữ ID ổn định; chỉ cập nhật RichEdit của tin đổi nội dung, không dựng lại toàn bộ hội thoại mỗi lần streaming. Giữ điểm đọc theo tin đang hiển thị; cuộn chuột/Page Up/Page Down dùng chung một vùng cuộn.
- Lần đầu tải 80 tin gần nhất, có nút tải tin cũ. Tin quá dài xem đầy đủ qua Chi tiết; dữ liệu lưu và bản sao chép toàn bộ không bị rút gọn.
- Sửa nút Hub Code/Chat bị cắt đáy bằng chiều cao hàng theo DPI và bỏ AutoSize ở nút dock.
- Bật double buffering native cho cây project; hover chỉ invalidate hai dòng liên quan. Các màn hình nhật ký/chi tiết dùng cập nhật văn bản giữ selection/viewport và tạm dừng redraw.

## 5.1.7 — 2026-10-14

- Tin nhắn người dùng nằm bên phải, mọi agent nằm bên trái; yêu cầu bổ sung trong phiên nhóm cũng thuộc phía người dùng.
- Cột tin nhắn người dùng co theo chiều rộng/DPI, giữ Markdown, chọn/sao chép, liên kết và cuộn native. Thay đổi kích thước cửa sổ dựng lại bố cục sau khoảng nghỉ ngắn.

## 5.1.6 — 2026-10-14

- Sửa kẹt chuyển lượt khi CLI đã thoát nhưng tiến trình nền còn giữ stdout/stderr. Hub đợi tiến trình CLI, thu nốt output tối đa 3 giây rồi dừng chờ luồng nền; thời gian hủy I/O tối đa thêm 1 giây.
- Giữ output đã nhận và exit code thật; không dừng ADB/dịch vụ nền chỉ để đóng pipe. CLI còn chạy vẫn dùng timeout/hủy như trước.

## 5.1.5 — 2026-10-14

- Profile mới không tạo Codex/Antigravity mặc định; người dùng tự chọn agent, provider và kết nối. Có hướng dẫn thêm agent đầu tiên; một agent hoặc nhóm tùy chọn được hỗ trợ.
- Migration cấu hình JS chỉ khôi phục agent cũ có trong dữ liệu, không tự chèn một cặp mặc định. Cấu hình native hiện có được giữ.
- Thêm `-PackageOnly`: tạo ZIP mà không thay app-bin, shortcut hoặc task đang chạy.
- Sắp xếp README/hướng dẫn phát triển/bảo mật; bổ sung ignore cho dữ liệu runtime và chuẩn bị workflow build GitHub với quyền đọc repo.

## 5.1.4 — 2026-10-14

- Hub Code giữ nguyên quyền CLI đã chọn cả khi thảo luận/review/sửa schema.
- Bỏ câu prompt chỉ cho terminal đọc dù người dùng chọn toàn quyền.

## 5.1.3 — 2026-10-14

- Phân biệt lỗi quyền công cụ headless với lỗi đăng nhập Antigravity.
- Kiểm tra kết nối/Tải model dùng `agy models` thay `--version`.
- Ưu tiên công cụ đọc/tìm file tích hợp khi terminal chưa được cấp phép.

## 5.1.1–5.1.2

- Sửa chữ/icon bị cắt ở DPI cao, cuộn ngang thư viện, cuộn hội thoại và giữ vị trí đọc khi streaming.
- Cho agent đọc project trong Hub Code thay vì cấm toàn bộ terminal khi bàn luận.

## 5.0–5.1

- Native C#/.NET, giao diện hội thoại theo bố cục Codex, thư viện project/topic và TXT.
- Agent CLI/API/thủ công, model/quyền riêng, quota fallback và tiếp tục hội thoại.
- Bàn tròn ngang hàng, task/phụ thuộc, tài liệu, Tester/Reviewer và kiểm tra tùy chọn.

Các bản sửa 5.1.x được kiểm tra biên dịch/đóng gói cục bộ; chưa chạy harness/chức năng/UI theo yêu cầu của người dùng. Xem CONTRIBUTING.md cho cách chạy harness độc lập.
