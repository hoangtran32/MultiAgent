# Chatbot từ extension, dùng tài khoản trình duyệt

Từ 5.9.0, extension nhận diện website và model từ tab người dùng chọn, chuyển phản hồi đã chuẩn hóa về Hub qua Native Messaging. Không dùng API key hay CLI. Model/quota theo tài khoản website. Gemini/ChatGPT có adapter UI riêng; website HTTPS khác dùng adapter chung **thử nghiệm**, chỉ chạy được khi cấu trúc chat/luồng phản hồi nhận diện được, không phải hỗ trợ mọi website.

## Kết nối lần đầu

1. Trong Hub, mở **Agent & model → Chatbot từ trình duyệt… → Chuẩn bị bridge & mở thư mục…**. Thao tác xuất extension đi kèm EXE, đăng ký native host ở HKCU cho tài khoản Windows hiện tại, không cần admin. Chưa gửi prompt/đăng nhập thay bạn.
2. Mở `chrome://extensions` hoặc `edge://extensions`, bật **Developer mode → Load unpacked**, chọn thư mục `extension` vừa mở. Tiện ích tên **Multi-Agent Hub · Browser Chat**, ID `koeckebjfmkgjanknbifdcgahkbbalpb`. Chrome yêu cầu người dùng chủ động cài; EXE không cài ngầm.
3. Mở **chat mới, trống** trên website chatbot, đăng nhập và chọn model tại website. Bấm extension để tự quét rồi **Kết nối**. Để **Nhãn riêng** trống để theo website, không cần nhập tên model. Nếu trang công khai tên model trong giao diện hoặc phản hồi, extension tự gửi tên đó cho Hub. Chưa công khai thì ghi rõ chưa nhận diện, không đoán model. Xác nhận chat mới trước khi nối; chỉ báo nối sau khi bắt tay với Hub.
4. Chọn một hoặc nhiều tab trong hộp **Chatbot từ trình duyệt** rồi **Dùng các chatbot đã chọn**. Hub chuyển sang **Hub Chat → Bàn luận qua lại**, giữ lựa chọn agent Hub Code riêng. Nhập chủ đề rồi gửi; không tự chạy chỉ vì đã nối tab.

Không tạo sẵn cặp agent cho profile mới; danh sách từ tab bạn nối. Không xóa thư mục extension sau khi Load unpacked. Đổi chỗ EXE thì chạy lại **Chuẩn bị bridge** để cập nhật đường dẫn native host.

## Trò chuyện và Làm việc từ 5.8.0

**Trò chuyện / Hub Chat** không cần project: nhập chủ đề, chọn chatbot rồi gửi. Sidebar chỉ liệt kê hội thoại của mục này; có ghim, đổi tên, lưu trữ, tìm kiếm và tiếp tục. JSON/TXT vẫn được lưu trong dữ liệu Hub. Dòng project và nút thêm folder được ẩn; project của Làm việc không bị thay đổi. File văn bản bạn chủ động đính kèm có thể ở bất kỳ thư mục nào (tối đa 512 KB).

**Làm việc / Hub Code** giữ project, task, quyền CLI và các chế độ thực hiện/review. Khi cần dùng chatbot web để hỗ trợ, chuyển sang Làm việc và tự chọn chatbot trong bộ chọn agent cạnh nút gửi. Chatbot chỉ nhận văn bản: tư vấn, code mẫu hoặc phản biện theo nội dung được cung cấp; không có công cụ đọc/sửa file hoặc chạy terminal trên máy. CLI hoặc bạn áp dụng/kiểm tra kết quả. Hub không tự gửi nội dung công việc lên web khi agent hết quota. Chủ đề công việc mới cũng cần tab web trống chưa gắn với hội thoại khác.

Chuyển giữa hai mục tạo phiên nhập mới, không nối tiếp nhầm hội thoại/task của mục kia; mở lịch sử của đúng mục để tiếp tục. Chat cũ được nhận diện theo trường surface trong job, tách khỏi project khi đọc; nội dung cũ không bị xóa/viết lại.

## Tự quét từ 5.7.1

Mỗi lần mở popup, extension đọc các ô nhập đang hiển thị, bỏ ô tìm kiếm/ô khóa và ưu tiên vùng soạn chat. Nút gửi được dò lại sau khi có nội dung, vì website có thể ẩn nút này khi ô nhập trống. Quét không nhập thử, bấm gửi hay thay model. Nhãn model lấy từ điều khiển trên giao diện khi xác định được duy nhất; không suy đoán từ lịch sử chat và không xác minh backend.

Nếu chưa nhận diện chắc, bấm **Tự quét lại** để bỏ CSS ô nhập/nút gửi cũ. Nếu vẫn có nhiều ô hợp lệ, bấm **Chọn ô nhập trên trang**, rồi bấm đúng ô soạn chat được viền xanh; mở lại popup để kết nối. Esc hủy chọn, bộ chọn tự đóng sau 30 giây. Lựa chọn trực tiếp chỉ có hiệu lực trên trang hiện tại; không áp dụng nhầm sang tab khác. CSS nội dung phản hồi nâng cao vẫn được giữ.

Sau khi cập nhật EXE, bấm **Chuẩn bị bridge & mở thư mục…** để xuất extension mới nếu thư mục chưa được cập nhật. Trong `chrome://extensions` hoặc `edge://extensions`, bấm **Reload** của tiện ích, sau đó **tải lại trang chatbot** và mở chat mới để nối lại. Script đã tiêm ở phiên bản cũ không được thay chỉ bằng việc mở lại popup. Không tải lại tab đang tạo phản hồi; đợi hoàn tất trước.

## Chrome đã liên kết nhưng Hub chưa nhận tab

Từ 5.8.2, trạng thái `loading`/`complete` của Chrome không tự ngắt tab. Extension kiểm tra nonce của document và URL hiện tại; chỉ chấp nhận URL cuộc chat mới sau khi chính lượt Hub vừa gửi được content script xác nhận. Khi tải lại thật, đổi cuộc chat khác hoặc trình duyệt discard tab, binding vẫn bị ngắt để tránh gửi nhầm/ngầm gửi lại. Nội dung đã nhận được giữ trong Hub và website.

Từ 5.8.1, mở popup extension sẽ kiểm tra và tự khôi phục cầu nối với Hub cho các tab đã gắn còn đúng URL/document. Có thể bấm **Đồng bộ lại với Hub** để thực hiện ngay. Không cần mở chat mới hoặc đăng nhập lại khi document cũ còn hợp lệ. Đồng bộ không gửi prompt, không replay lượt bị gián đoạn; không thực hiện khi còn request đang chạy. Tab tải lại/đổi chat cần liên kết mới.

Trạng thái **tab đã liên kết** là danh sách tab extension đang giữ; dòng trạng thái cầu nối cho biết Hub có đang mở kết nối hay chưa. Nếu Hub đã nhận danh sách nhưng agent vẫn giữ ID cũ, bấm Gửi sẽ mở thiết lập đúng agent: chọn tab hiện tại ở **Tab chatbot**, lưu rồi Gửi lại. Yêu cầu nhập và hội thoại được giữ nguyên; Hub không tự chọn tab khác theo tên model. Bấm Kết nối nhiều lần trên cùng document/model từ bản này giữ nguyên ID để không lặp lỗi đó.

## Trò chuyện nhóm từ 5.10.0

Trong **Hub Chat → Trò chuyện nhóm**, người dùng và các chatbot ở cùng một cuộc nói chuyện. Web là transport gửi/nhận như API; tính cách/cách điều phối nằm trong engine của Hub, không do việc parse stream quyết định. Chỉ tạo agent từ tab bạn đã kết nối; không gán model hay danh sách nhân vật cố định.

Ví dụ bạn gửi **hello**: A chào bạn và B; B thấy lời của A, chào bạn và A. Không ép lời chào thành báo cáo nghiên cứu. Khi bạn đưa vấn đề, A nêu một ý, B trả lời/bổ sung/hỏi A, A phản hồi…; các bot tiếp tục cho đến khi không còn ý hữu ích hoặc cần thông tin từ bạn. Bạn không phải chuyển lời hay bấm Tiếp tục sau mỗi bot. Không có vai cấp trên bắt buộc, không yêu cầu sổ ý tưởng/bàn giao ở mỗi tin. Kết quả vẫn là ý kiến model; không bảo đảm giải pháp đúng chỉ vì cả hai đồng ý.

Mỗi lượt thường 1–3 đoạn ngắn để phản hồi được ngay. Bot có thể chỉ định đồng đội đáp tiếp, với cơ hội nói cho mọi thành viên. Tin của bạn chen vào được lưu theo đúng thứ tự và gửi ở lượt kế tiếp; không hủy hoặc ghi đè câu trả lời đang chạy. Nếu một bot lỗi/hết quota, giao diện vẫn báo rõ, không nói hộ hoặc giả rằng bot đó đã đồng ý.

Khi mọi bot còn kết nối đã phát biểu và đều chọn chờ, giao diện hiện **Chờ bạn**. Đây là trạng thái nhàn, không gọi model thêm, không phải lỗi hay chứng nhận task hoàn tất. Gửi tin nhắn để đánh thức ngay; không có thời gian nghỉ bắt buộc. Khi còn ý cần nhóm trao đổi, luân phiên tiếp mà không cần bạn nhắn thêm. Nếu model không gửi tín hiệu điều phối hợp lệ, engine tiếp tục theo lượt thay vì tự kết luận cả nhóm đã xong. Tín hiệu điều phối của lượt được ẩn khỏi nội dung chat.

**⋯ → Kết thúc & tổng hợp** chờ câu đang trả lời rồi tổng hợp những gì đã thực sự bàn; **Dừng** dừng vòng Hub ngay. Nếu website vẫn tạo phản hồi, dừng trực tiếp tại tab. Giới hạn lượt/thời gian tùy chọn vẫn áp dụng; mặc định 0 là không giới hạn, quota website độc lập. Hub Code giữ cách bàn luận nghiên cứu hiện có.

Mỗi binding tab thuộc một chủ đề Hub. Chủ đề mới cần chat web mới trống; mở lịch sử Hub để tiếp tục chủ đề cũ. Nếu tab cũ mất nối, kết nối tab trống mới rồi mở lịch sử; ngữ cảnh được gửi từ bản ghi. Giữ Hub/tab mở, không gõ thêm hoặc đổi chat trực tiếp trong tab đã nối. JSON/TXT lưu các tin hiển thị, không hiển thị footer điều phối nội bộ.

## Đọc luồng phản hồi và model

Extension cài observer `fetch`/XHR trong MAIN world của **tab đang được chọn**, qua quyền activeTab hiện có. Observer chỉ được kích hoạt khi Hub chuẩn bị gửi một lượt; chỉ đọc bản sao phản hồi POST cùng origin có body chứa đúng prompt vừa gửi. Không gọi lại endpoint đã bắt được, không xuất API key, cookie, Authorization header hay phiên đăng nhập sang app. Website vẫn tự gửi yêu cầu bằng phiên đăng nhập của nó qua nút Gửi; app nhận văn bản, model quan sát được, nguồn nhận diện, kiểu luồng và dấu hiệu hoàn tất.

`network-protocol.js` có bộ đọc các dạng nhận diện được: SSE/JSON/NDJSON Chat Completions, Responses, Messages, snapshot tin assistant của ChatGPT, generate-content, envelope/candidate RPC Gemini. Đây là **decoder theo cấu trúc**, không có danh sách model cố định. Chỉ đọc phần trả lời, bỏ tool calls/thoughts; không giải nghĩa mọi giao thức riêng. Model ID từ phản hồi được đánh dấu **phản hồi web**, nhãn từ picker là **giao diện**, nhãn riêng là **nhãn người dùng**. Chúng không phải chứng thực backend. Chọn/đổi model trực tiếp trên website; khi để nhãn trống, metadata nhận diện cập nhật theo lượt mà không làm mất liên kết.

Mỗi lượt có request ID, document nonce, kiểm tra URL và tin người dùng mới. Khi parse được tín hiệu hoàn tất rõ ràng cùng nội dung, Hub chốt lượt; EOF/chữ đứng yên không đủ. Gemini RPC chưa có tín hiệu hoàn tất ổn định thì dùng trạng thái UI, không coi đóng stream là thành công. Nếu observer không bắt được fetch đã được website giữ sẵn, request khác origin, WebSocket hoặc cấu trúc không biết, tiếp tục dùng DOM. Website đổi UI có thể cần CSS nâng cao cho tin người dùng, tin AI và trạng thái hoàn tất. Nếu vẫn chưa rõ thì giữ partial, báo lỗi và không replay prompt.

MAIN world và DOM do website kiểm soát: payload được coi là dữ liệu không tin cậy, giới hạn 4 MiB luồng / 64.000 ký tự trả lời / 120 ký tự model, không được thực thi lệnh Hub. Không đọc tài khoản hay model của các tab chưa được người dùng liên kết. Không dùng debugger, all-sites permission hoặc đọc kho credential.

## Giới hạn cần biết

Đây là bridge DOM + observer phản hồi **thử nghiệm**, không phải API chính thức của website. Không thể lấy API công khai chỉ bằng một tài khoản web hay bảo đảm một parser chạy mọi chatbot. Bản này build/cú pháp cục bộ và kiểm tra fixture offline qua CI; **chưa kiểm thử gửi/nhận trên website live**. Trang thay đổi hoặc che model có thể khiến nhận diện không đầy đủ. Không vượt đăng nhập/CAPTCHA hoặc quota. Không gõ thêm/đổi cuộc chat trong tab đang nối; không ghi đè bản nháp hoặc tự gửi lại khi mất kết nối.

## Gỡ bridge

Gỡ extension tại trang quản lý tiện ích. Có thể xóa đúng subkey `com.multiagenthub.browser_chat` dưới `HKCU\Software\Google\Chrome\NativeMessagingHosts` và `HKCU\Software\Microsoft\Edge\NativeMessagingHosts`; sau đó xóa `%LOCALAPPDATA%\MultiAgentHub\browser-bridge`. Không xóa thư mục dữ liệu Hub để tránh mất lịch sử.

## Tài liệu thiết kế

- [Chrome Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging) và [activeTab](https://developer.chrome.com/docs/extensions/develop/concepts/activeTab).
- [Chrome scripting / MAIN world](https://developer.chrome.com/docs/extensions/reference/api/scripting) và [Response.clone](https://developer.mozilla.org/en-US/docs/Web/API/Response/clone).
