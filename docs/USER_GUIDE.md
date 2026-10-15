# Hướng dẫn Multi-Agent Hub 5.3.1

Profile mới bắt đầu không có agent: bấm **Thêm agent của bạn**, chọn công cụ/kết nối rồi cấu hình model, tài khoản và quyền. Dùng một agent bất kỳ hoặc nhóm tùy chọn; không cần Codex hay Antigravity. Cấu hình hiện có được giữ khi nâng cấp. Hub Code giữ nguyên quyền CLI trong mọi lượt, Hub Chat chỉ trao đổi văn bản.

Ứng dụng Windows bằng **C# / .NET 10 WinForms** để phối hợp các công cụ AI, với model, vai trò và quyền riêng cho từng agent. Desktop không dùng Node.js, JavaScript, web server hay WebView2.

## Bàn tròn & thực hiện

Chọn **Bàn tròn & thực hiện** dưới ô nhập, mở **Agent & model → Phân vai & giới hạn bàn tròn…**, rồi gửi yêu cầu. Các model trao đổi ngang hàng: phát triển ý tưởng của nhau, giữ bất đồng có lý do, đề xuất task có tiêu chí và phụ thuộc. Một task cần được thành viên khác chấp nhận trước khi thực hiện; khi chỉ còn một kết nối, nó tự rà soát và kết quả ghi rõ thiếu review độc lập. Chọn người nói tiếp theo bằng đề xuất của model trong các thành viên chưa phát biểu của vòng, tránh một model chiếm hết lượt.

Năm chuyên môn **Product Manager / Architect / Coder / Tester / Reviewer** không phải năm cấp bậc và không đòi hỏi năm tài khoản. Mỗi vai chọn kết nối/model ưu tiên; để Tự phân công để chia chuyên môn trong nhóm. Hub luân phiên góc nhìn khi một kết nối đảm nhiệm nhiều vai. Sau thực hiện, Tester phân tích kiểm chứng, Reviewer phản biện rồi kết quả được đưa lại ra bàn tròn; không khóa toàn phiên vào chuỗi chức danh.

**Hội thoại** hiển thị các lượt và vai trò. **Công việc & đầu ra** chứa task, phụ thuộc, tiêu chí, kết quả, phản biện, tài liệu, bằng chứng lệnh; có **Xuất tài liệu…** ra thư mục riêng. Khi đang chạy, nhập yêu cầu và bấm **Bổ sung**: nội dung được áp dụng tại ranh giới bước tiếp theo. Task đã hoàn tất được đưa ra rà soát lại nếu phạm vi thay đổi. **Dừng sau bước** kết thúc sau bàn tròn hoặc cụm thực hiện/kiểm tra/review đang làm; **Dừng** hủy ngay. Mở hội thoại cũ và gửi tiếp để giữ bảng task và đầu ra; các task chưa hoàn tất cần được xác nhận lại, không tự chạy lại khi mở app.

Coder sửa file cần CLI có quyền **workspace-write/full**; Hub không tự nâng quyền. API thuần văn bản tham gia bàn tròn, tài liệu và review, không tự có công cụ sửa file. Antigravity ở quyền `default` cần được đặt `workspace-write` nếu bạn muốn nó nhận task sửa code. Chế độ Trò chuyện chỉ bàn luận/lập kế hoạch. Kết nối dán kết quả thủ công không tham gia phiên nhóm tự động.

Mặc định **bạn tự kiểm tra**: Hub không chạy lệnh build/test, ghi trạng thái **Chưa kiểm chứng** cho kết quả sửa code. Trong **Agent & model → Phân vai & giới hạn bàn tròn… → Lệnh kiểm tra**, có thể cấu hình executable, mảng đối số JSON và timeout, rồi bật tự kiểm tra cho project hiện tại. Hub chạy đúng lệnh đã cấu hình; model không được tự thêm lệnh vào danh sách thực thi. Tester/Reviewer giữ quyền CLI đã chọn, tập trung đối chiếu bằng chứng và chuyển đề xuất sửa về task thực hiện. CLI tùy chỉnh tự quản lý quyền. Lệnh build/test có thể thực thi script project, nên chỉ cấu hình lệnh bạn tin cậy.

Bằng chứng lưu exit code, log và fingerprint source (ngoài các thư mục sinh tự động). Sửa source làm hết hiệu lực kết quả trước; lệnh đạt không đồng nghĩa tất cả tiêu chí đã được bao phủ. Khi không xác minh được fingerprint, Hub không ghi thành đã kiểm chứng. Nhóm dùng một writer tại một thời điểm và khóa workspace giữa các phiên nhóm; chưa chạy nhiều coder trong các worktree riêng.

Giới hạn mặc định: **30 lần gọi, 120 phút, 12 task, 2 vòng sửa/task**; chỉnh được. Lượt lỗi, sửa định dạng và fallback đều tính vào số gọi. Quota bỏ chọn kết nối, chuyển sang thành viên còn lại đủ khả năng; không bỏ qua bước review bắt buộc. Hết khả năng/quá giới hạn thì lưu kết quả còn dang dở. Dữ liệu nhóm nằm trong JSON hội thoại/checkpoint và bản TXT đang có, không ghi credentials vào cấu hình nhóm.

Hướng dẫn nhóm: [TEAM_USER_GUIDE.md](TEAM_USER_GUIDE.md). Kiến trúc và phát triển tiếp: [ARCHITECTURE.md](ARCHITECTURE.md).

## Giao diện hội thoại

Trong **Agent & model → Dò công cụ & model**, Hub tự tìm CLI đã cài, lấy danh sách model và ghi nguồn. Quản lý agent trống mở màn hình dò trước. Chọn kết quả rồi Lưu để tạo/cập nhật agent; kết quả dò và tài khoản không tự được thêm/bật. Nút Dò lại cập nhật kết quả. API chỉ dò các endpoint đã cấu hình, không tìm tài khoản/API key trên máy. CLI tùy chỉnh không có giao thức model chung nên vẫn nhập ID/đối số nếu cần.

Thiết lập agent tự dò model khi mở/đổi kết nối; vẫn đổi kết nối hoặc lưu trong lúc dò. Antigravity dùng `agy models`; Codex đọc cache model; Claude đọc model khai báo trong cấu hình/biến môi trường thay vì gợi ý sonnet/opus/haiku cố định. Nguồn cục bộ không chứng minh model còn quyền truy cập; không có danh sách thì giữ lựa chọn đã nhập hoặc dùng mặc định CLI. Không gửi prompt suy luận để dò. Agent đang tắt do quota không tự bật lại. Khi phiên đang chạy/chờ dán, cần dừng phiên trước khi thay cấu hình agent; không tự chuyển hoặc chạy lại lượt cũ.

Bản 5.1 dùng giao diện sáng theo bố cục Codex: thanh biểu tượng bên trái, project và hội thoại gần đây, nội dung giữa màn hình không có khung tab. Chuyển **Hub Code / Hub Chat** tại đầu thanh bên. Tìm kiếm mở từ biểu tượng kính lúp hoặc Ctrl+Shift+F. Tiêu đề dài tự rút gọn theo chiều rộng, di chuột để xem đầy đủ.

Từ 5.1.7, tin nhắn của bạn và phần bổ sung yêu cầu hiện bên phải; các agent và bản tổng hợp hiện bên trái. Áp dụng cho cả hội thoại cũ và mới, với bất kỳ số lượng agent nào. Code block vẫn căn trái bên trong phần tin nhắn để dễ đọc.

Từ 5.1.8, tin của bạn có bong bóng xanh nhạt; AI có khung riêng, ngăn cách metadata/nội dung/thao tác bằng đường mờ. Nút **Sao chép** lấy toàn bộ nội dung tin, **Chi tiết** mở nội dung đầy đủ và nhật ký/prompt agent trong phiên nếu có. Nhấp phải tin để sao chép phần chọn hoặc mở Chi tiết. Nhật ký và prompt gần nhất được ghi nhãn theo agent/phiên, không giả lập log riêng cho lượt cũ.

Hội thoại dài tải 32 tin gần nhất trước; bấm **Hiển thị tin nhắn cũ** để tải thêm mỗi đợt 32 tin. Một tin quá dài hiển thị trích tối đa 24.000 ký tự trong khung; mở Chi tiết để đọc đầy đủ. Giới hạn này chỉ áp dụng giao diện, không cắt dữ liệu JSON/TXT hay bản sao chép. Cuộn chuột và Page Up/Page Down đi qua toàn bộ khung tin; Ctrl+Home/Ctrl+End đến đầu/cuối. Đang đọc phần cũ thì streaming giữ điểm đọc; bấm mũi tên xuống để theo cuối. Ba chat gần nhất giữ lại khung native và vị trí đọc khi chuyển qua lại; chọn chat khác hủy lượt tải cũ, không tự chạy lại model.

Nhấp phải folder project (hoặc Shift+F10/phím Menu) để **Ghim**, **Chỉnh sửa** tên hiển thị, chọn/tạo **Mục**, **Mở trong File Explorer**, **Lưu trữ các cuộc trò chuyện** và **Gỡ dự án**. Tên hiển thị không đổi tên folder trên ổ đĩa; Mục chỉ tổ chức sidebar. Project ghim nằm trong nhóm riêng. Lưu trữ toàn bộ chat của project giữ nguyên JSON/TXT; bật Hội thoại đã lưu trữ rồi dùng menu project để khôi phục. Gỡ chỉ ẩn khỏi thư viện, giữ nguyên thư mục/chat và các tùy chọn tổ chức. Thêm lại cùng đường dẫn để khôi phục project. Hội thoại đang mở vẫn có thể đọc sau khi lưu trữ.

Gửi tin mới hoặc bổ sung yêu cầu sẽ đưa đến cuối hội thoại. Có thể lăn chuột trên nội dung, phần nền hoặc thao tác của khung tin để đọc tiếp; giới hạn thanh cuộn được cập nhật khi tin mới xuất hiện hoặc nội dung tăng chiều cao.

Ô nhập bo góc chứa nút **+** để đính kèm/thay/bỏ file, cách phối hợp, chọn agent tham gia và nút gửi hình mũi tên. Chỉ giữ **Agent & model** ở cuối sidebar để quản lý agent, phân vai/giới hạn bàn tròn và giới hạn bàn luận. Rê chuột trên cách phối hợp đang chọn hoặc từng mục trong menu để đọc mô tả cơ chế và ví dụ. Khi bàn tròn đang chạy, **⋯ → Dừng sau bước hiện tại** kết thúc sau bước đang làm; nút Dừng cạnh ô nhập hủy ngay. Nút **⋯** trên đầu hội thoại mở chi tiết/nhật ký, sao chép, TXT và quản lý topic. Nút danh sách ở góc phải mở **Công việc & đầu ra** thành khu vực phụ; cửa sổ hẹp mở thành hộp riêng. Markdown cơ bản (tiêu đề, chữ đậm/nghiêng, liên kết, danh sách, code) được hiển thị bằng RichEdit native.

Ô nhập tăng chiều cao theo số dòng đến giới hạn rồi cuộn nội dung bên trong. Gõ chữ hoặc Space trên cùng dòng không đổi chiều cao bố cục. Khi nhóm đang chạy, bạn vẫn nhập bổ sung mà không bị tắt/bật lại ô nhập theo từng lượt streaming.

Thanh bên giữ project, đoạn chat mới, tìm kiếm và hội thoại đã lưu. Nội dung của bạn và các agent xuất hiện chung ở giữa; ô nhập luôn ở dưới. Bộ chọn agent ngay cạnh mũi tên gửi bật/tắt từng thành viên, hiển thị model; đổi model/vai trò trong Agent & model. **Agent & model** ở cuối thanh bên mở quản lý đầy đủ (thêm, nhân bản, đổi thứ tự, cấu hình CLI/API). **⋯ → Chi tiết** chứa câu trả lời riêng, nhật ký, prompt gửi đi, lịch sử gần đây, dùng lại yêu cầu và xuất Markdown. Mũi tên xuống trên đầu hội thoại đưa đến nội dung cuối; cuộn lên để đọc phần cũ. **Ctrl + Enter** gửi, Enter xuống dòng.

Chuyển **Hub Code / Hub Chat** ở đầu thanh bên. Code (Làm việc) gửi đường dẫn project và chạy CLI tại thư mục đó, giữ nguyên quyền riêng đã cấu hình trong mọi lượt, gồm thảo luận, bàn tròn, Tester/Reviewer và sửa định dạng JSON. Chọn `full` cho phép file/terminal theo yêu cầu, không bị tự hạ về chỉ đọc; chọn `read-only` thì giới hạn đọc vẫn áp dụng. Vai trò xác định công việc của lượt: nhóm đưa thay đổi project vào task thực hiện và review đối chiếu kết quả. Chat (Trò chuyện) yêu cầu trả lời trực tiếp, không dùng công cụ/đọc/sửa file/chạy lệnh và dùng bản sao quyền read-only/Plan cho CLI. API thuần văn bản không có công cụ đọc thư mục trên máy dù cấu hình quyền nào. CLI tùy chỉnh và app thủ công tự quản lý quyền ở công cụ. Đây là hai chế độ của Hub dùng các agent đã kết nối, không phải tích hợp hay đăng nhập thêm tài khoản ChatGPT. Chế độ được lưu theo từng lần trao đổi và khôi phục khi mở hội thoại.

Nhấp phải topic để **Ghim**, **Đổi tên**, **Lưu trữ** hoặc mở `.txt`. Bật **Hội thoại đã lưu trữ** để xem và khôi phục topic; lưu trữ giữ nguyên nội dung. Ghim hiển thị ở đầu thư viện. Đổi tên giữ đường dẫn file `.txt` ban đầu để các liên kết đã có vẫn dùng được.

**Cùng làm việc** chạy agent chính → các đồng đội rà soát → quay lại agent khả dụng đầu tiên để hoàn thiện sau góp ý. Hai agent có tối đa 3 lần gọi, một agent chỉ 1 lần; nhiều agent có tối đa `số agent + 1` lần. Mỗi lượt nhận kết quả trước, lưu riêng trong transcript; chạy lần lượt để tránh cùng sửa một file. Hết quota tự bỏ chọn và bỏ qua; lỗi thường dừng chuỗi. **Song song** cho các agent cùng nhận yêu cầu; **Lần lượt / phản biện** chỉ đi qua đội một lượt; **Bàn luận qua lại** chạy phiên nhiều lượt và tổng hợp theo giới hạn đặt. Trạng thái chờ ghi rõ agent đang đợi đồng đội nào.

## Chạy ứng dụng

Giải nén toàn bộ `MultiAgentHub-win-x64.zip`, mở **`MultiAgentHub.exe` ngay trong thư mục vừa giải nén**. Bản Windows x64 tự chứa runtime .NET, không cần cài SDK để sử dụng. Trong repo, `Open_App.bat` mở bản build ở `app-bin/`. Icon được nhúng trong executable.

## Công cụ và kết nối

| Công cụ | Kết nối | Model và kết quả |
|---|---|---|
| Codex | Codex CLI hoặc thủ công | Nhập model ID hoặc chọn từ cache CLI; mức suy luận và quyền riêng. |
| Claude / Anthropic | Claude Code CLI, Anthropic API, app/web thủ công | CLI nhận alias/model ID; API tải danh sách model bằng key của bạn. |
| Google Antigravity | **IDE**, **2.0 desktop**, **CLI agy** riêng biệt | IDE/desktop: chọn model trong ứng dụng, gửi prompt rồi dán kết quả về Hub. CLI tự động khi đã cài. |
| Google Gemini | Gemini API hoặc app/web thủ công | API tải danh sách model, app/web dùng sao chép/dán. |
| API tương thích OpenAI / local AI | Chat Completions API | Base URL, model ID và key khi dịch vụ yêu cầu; hỗ trợ HTTP localhost. |
| Công cụ AI khác | Executable/script CLI hoặc thủ công | CLI tùy chỉnh để mở rộng mà không cần sửa Hub. |

Hub dùng các CLI đã cài và đăng nhập trên máy, không kèm công cụ AI. Danh sách model phụ thuộc tài khoản và endpoint; luôn có thể nhập model ID trực tiếp. **API chỉ nhận yêu cầu và file đính kèm, chưa có công cụ tự đọc/sửa project hoặc chạy terminal.** Dùng CLI để agent trực tiếp làm việc với source.

Antigravity CLI trên Windows: dùng [bộ cài chính thức](https://antigravity.google/docs/cli/install/), mở terminal mới sau khi cài, chạy `agy` để đăng nhập nếu cần. Hub tự tìm `%LOCALAPPDATA%\agy\bin\agy.exe`, không cần PATH của phiên hiện tại đã cập nhật. Chọn **Antigravity CLI (agy)** trong thiết lập agent; **Tải model** gọi `agy models` và không gửi prompt suy luận. CLI dùng giao thức [stream-json chính thức](https://antigravity.google/docs/cli/headless/) qua stdin/stdout.

Nếu Antigravity không trả lời và nhật ký có `headless`, `command permission`, `auto-denied`, đó là lỗi quyền công cụ. Chạy ngầm không hiện hộp xin phép; `default` và `Accept edits` không tự duyệt mọi lệnh terminal. Đọc source bằng công cụ đọc/tìm file tích hợp. Để chạy các lệnh cụ thể, cấu hình `permissions.allow` trong `%USERPROFILE%\.gemini\antigravity-cli\settings.json` theo [hướng dẫn chính thức](https://antigravity.google/docs/cli/headless/#permissions-in-headless-mode). Hoặc chọn **Agent & model → Thiết lập / model → Quyền CLI → Tự phê duyệt • toàn quyền** để cho phép mọi công cụ trong Hub Code, kể cả thảo luận/review. Hub không tự đổi quyền đã lưu. Có danh sách model chỉ xác nhận endpoint model hoạt động, chưa chứng minh quota suy luận hoặc quyền công cụ; chỉ đăng nhập lại khi CLI thực sự báo yêu cầu xác thực.

## Project, topic và hội thoại

Thanh bên lưu các project và topic theo project. **+ Project** thêm thư mục; **+ Chat mới** tạo topic mới. Nhập **Tên topic** trên đầu hội thoại, hoặc để trống để lấy dòng đầu yêu cầu. Có ô tìm topic/project; kéo thanh phân chia dọc để đổi chiều rộng thư viện.

Bấm topic để xem toàn bộ yêu cầu/kết quả đã lưu. Nhập câu hỏi mới rồi **Gửi tiếp** / **Tiếp tục thảo luận**; nút **Tiếp tục hội thoại** đưa con trỏ về ô nhập. Hub giữ nguyên topic và nối thêm lần trao đổi vào đó. Model/agent dùng cấu hình đang chọn; kiểm tra trước khi gửi, nhất là agent đang hết quota. Tiếp tục dùng ngữ cảnh do Hub dựng từ tối đa 8 lần trao đổi gần nhất, ưu tiên phần mới, tối đa khoảng 18.000 ký tự; không khôi phục phiên terminal gốc của từng nhà cung cấp. Thảo luận tiếp có một giới hạn lượt/thời gian mới theo cài đặt.

Mỗi topic có file UTF-8 `.txt` tự cập nhật, ví dụ **`Thiết kế ứng dụng_12-10-2026_02-15-30.txt`**. Ngày giờ lấy từ lúc tạo topic; các topic trùng tên/ngày nằm trong thư mục ID riêng nên không ghi đè nhau. Bấm **Mở bản .txt** để xem bằng Notepad. File nằm trong `%LOCALAPPDATA%\MultiAgentHub\conversations\<id>\`; không ghi vào source project. Lưu khi bắt đầu, khi mỗi agent/lượt kết thúc và khi tác vụ kết thúc; nội dung đang stream có thể chưa lưu nếu mất điện.

Thư viện hội thoại không bị giới hạn 50 tác vụ như danh sách lịch sử gần đây. Lịch sử cũ được nhập vào thư viện khi mở bản mới, không xóa file gốc. Nếu app đóng bất ngờ, phần đã lưu hiện là **Phiên bị gián đoạn**; bạn có thể gửi yêu cầu mới để tiếp tục, Hub không tự chạy lại. Project bị chuyển/xóa vẫn xem được hội thoại; cần khôi phục thư mục trước khi tiếp tục chạy.

## Sử dụng

1. Chọn project từ thanh bên hoặc thêm thư mục bằng nút **+** cạnh **Dự án**.
2. Mở **Agent & model**, chọn **Công cụ AI**, **Kết nối**, **Số lượng** rồi **+ Thêm agent**. Tối đa **32 agent**.
3. Chọn dòng → **Thiết lập / model** để đặt tên, model, vai trò và kết nối. Hộp thiết lập chỉ hiện trường phù hợp. **Kiểm tra kết nối** kiểm tra CLI/đường dẫn hoặc endpoint danh sách model, không gửi tác vụ suy luận.
4. Đánh dấu **Chạy** cho agent tham gia. Agent mới thiếu CLI/API được tắt để bạn cấu hình trước. **Nhân bản** tạo ID riêng, giữ cấu hình; key riêng không được nhân bản.
5. Chọn cách phối hợp dưới ô nhập; dùng **↑ ↓** trong quản lý agent để đổi thứ tự.
6. Nhập yêu cầu, tùy chọn đính kèm một file văn bản trong project (tối đa 512 KB), rồi **Gửi** hoặc **Ctrl + Enter**.

Song song gửi cùng yêu cầu, kèm vai trò riêng. Pipeline gửi thêm kết quả các bước trước. Chọn quyền phù hợp khi nhiều CLI cùng sửa project.

## Thảo luận ý tưởng / chạy qua đêm

Chọn các agent dùng **CLI/API**, chọn **Bàn luận qua lại**, mở **Agent & model → Giới hạn bàn luận…**, nhập chủ đề rồi gửi. Có thể dùng Codex CLI + Gemini API, Claude CLI + Antigravity CLI hoặc các model khác đã cấu hình. Codex CLI là một kết nối riêng với tài khoản trên máy; cuộc chat Codex hiện tại không tự tham gia. Model ID phụ thuộc quyền tài khoản, dùng **Tải model** trong thiết lập hoặc nhập ID hợp lệ.

- **Số lượt** tính mỗi lần một model đóng góp, bao gồm lượt lỗi. Các model chạy lần lượt theo thứ tự danh sách, đọc ý kiến trước và phát triển/phản biện ý tưởng. Một model vẫn dùng được để tự phản biện.
- **Thời gian** là giới hạn cả phiên; dành tối đa 2 phút cuối (10% với phiên ngắn) để tổng hợp. Phiên kết thúc khi chạm số lượt hoặc thời gian, điều kiện nào đến trước. Mẫu **8 giờ**: 48 lượt, tối đa 480 phút, nghỉ 600 giây giữa lượt; có thể kết thúc sớm hơn.
- Mỗi lượt tối đa 10 phút và được giới hạn bởi thời gian còn lại. Cuối phiên có tối đa một lần thử tổng hợp cho mỗi agent còn khả dụng; tổng số lần gọi không vượt `số lượt + số agent`. Đây không phải giới hạn tiền/token. CLI/API tiêu thụ quota/chi phí theo tài khoản.
- Hết quota: tự bỏ tích agent và tiếp tục với các model còn lại; còn một thì chạy đơn. Lỗi khác bỏ qua agent đó trong phiên để tránh lặp lỗi, không bỏ tích cấu hình. Hết tất cả agent thì kết thúc, không tự thử lại.
- Mở tab **Hội thoại & tổng hợp** để xem toàn bộ lượt và **Tổng hợp buổi sáng**: ý tưởng tốt, điểm đồng thuận/bất đồng, giả định, rủi ro và cách kiểm chứng. Nếu dừng hoặc không model nào tổng hợp được, Hub tạo bản ghi trích nội dung đã có, ghi rõ chưa có tổng hợp từ model. Có thể xem lại trong lịch sử hoặc xuất Markdown.

Giữ **Hub mở và máy không sleep/hibernate** khi chạy qua đêm. Hub lưu checkpoint trước/sau mỗi lượt. Nếu app tắt bất ngờ, vào **Lịch sử** xem phiên bị gián đoạn; không tự gửi lại yêu cầu hoặc tiếp tục tính phí. Nội dung đang stream giữa một lượt có thể chưa được lưu. Dừng phiên giữ phần đã hoàn tất và không gọi model để tổng hợp thêm.

Thảo luận trong **Hub Code** giữ nguyên quyền CLI từng agent, cho phép dùng công cụ theo yêu cầu người dùng và quyền đã chọn. Model tập trung trao đổi, phản biện và kiểm chứng; không tự triển khai ý tưởng chưa được yêu cầu. **Hub Chat** chỉ trao đổi văn bản. CLI tùy chỉnh tự cấu hình quyền tại công cụ. IDE/desktop qua sao chép/dán không hỗ trợ phiên thảo luận tự động. Vai trò mỗi agent vẫn áp dụng. Toàn bộ transcript lưu trong thư viện; ngữ cảnh gửi model gồm 6 lượt gần nhất và trích 30 lượt trước đó, tài liệu đính kèm tối đa 24.000 ký tự. Model được yêu cầu duy trì sổ ý tưởng để chuyển tiếp nội dung cũ; bản tổng hợp có thể bỏ sót ý tưởng, cần đọc lại và kiểm chứng.

Khi CLI/API báo rõ hết quota, usage limit hoặc hết credit, Hub tự bỏ tích **Chạy** của đúng agent đó và lưu cấu hình. Pipeline bỏ qua bước hết quota để các agent còn lại tiếp tục với yêu cầu gốc và kết quả thành công trước đó; song song giữ các agent khác đang chạy. Nếu chỉ còn một agent thì tiếp tục chạy đơn. Khi tất cả hết quota, tác vụ kết thúc và không tự thử lại. Lỗi mạng/model/quyền hoặc giới hạn tốc độ tạm thời không tự bỏ tích; lỗi thông thường vẫn dừng pipeline.

Với app/IDE thủ công, Hub không đọc được quota: bấm **Hết quota → bỏ chọn** trong tab **Dán kết quả** để bỏ qua agent và tiếp tục. Không dán thông báo quota như một câu trả lời thành công. Khi quota khôi phục, tự đánh dấu **Chạy** lại; Hub không tự bật agent. Nhận diện CLI dựa trên lỗi có cấu trúc hoặc thông báo quota rõ ràng khi CLI thất bại, nên thông báo mới/không rõ có thể vẫn được báo là lỗi thông thường.

Agent thủ công có tab **Dán kết quả**. **Sao chép prompt** lấy yêu cầu đầy đủ gồm model mong muốn, vai trò, file và kết quả trước. Antigravity có **Mở ứng dụng & sao chép**: IDE mở project/file prompt, desktop mở ứng dụng riêng. Bạn chọn model, gửi prompt ở đó, quay lại Hub dán câu trả lời và nhấn **Nhận kết quả & tiếp tục**.

Mỗi agent có câu trả lời, nhật ký và prompt đã gửi. Có thể sao chép kết quả, xem lịch sử, dùng lại yêu cầu hoặc xuất Markdown của lần trao đổi đang xem. Danh sách lịch sử gần đây giữ tối đa 50 tác vụ; thư viện thanh bên lưu riêng toàn bộ hội thoại. Kéo thanh phân chia để đổi kích thước vùng agent/yêu cầu/kết quả.

Chỉ chạy một tác vụ tại một thời điểm. **Dừng tác vụ** hủy tiến trình CLI và việc chờ dán kết quả; tác vụ đã gửi trong IDE/desktop cần dừng ở ứng dụng đó. Đóng Hub cũng hủy tác vụ đang chạy. CLI giới hạn 30 phút mỗi agent; HTTP API giới hạn 10 phút; luồng thủ công chờ đến khi bạn trả lời hoặc dừng.

## CLI tùy chỉnh

```text
Executable: C:\Python\python.exe
Đối số CLI (JSON): ["D:/tools/my_agent.py", "--model", "{model}", "--project", "{project}"]
```

Đối số truyền trực tiếp, prompt UTF-8 qua stdin, stdout là kết quả, stderr là nhật ký. Không chạy qua shell; `.cmd`/`.bat` không được hỗ trợ. Với script, chọn executable của runtime và đưa script vào đối số. Quyền CLI tích hợp không áp đặt sandbox cho executable tùy chỉnh.

## Dữ liệu và key

Thư mục riêng: `%LOCALAPPDATA%\MultiAgentHub`.

- `native-config.json`: agent, model, project, quyền và chế độ.
- `native-history.json`: yêu cầu, prompt đã gửi, kết quả, nhật ký của tối đa 50 tác vụ.
- `native-active-discussion.json`: checkpoint phiên thảo luận; xóa sau khi lưu lịch sử thành công.
- `conversations/<id>/`: metadata topic, từng lần trao đổi JSON và file `Tên topic_Ngày trò chuyện.txt`, không chứa API key.
- `handoffs/`: prompt tạm cho IDE, dọn khi tác vụ kết thúc.

Key riêng nằm trong **Windows Credential Manager**, không lưu trong JSON. Key đã lưu ưu tiên hơn biến môi trường. Hub chỉ tự dùng `ANTHROPIC_API_KEY`, `GEMINI_API_KEY`, `OPENAI_API_KEY` với host chính thức tương ứng; endpoint tùy chỉnh cần key riêng hoặc tên biến môi trường chỉ định rõ.

Cấu hình/lịch sử JS được đọc từ `config.json`/`history.json` trong thư mục dữ liệu khi chưa có bản native. File gốc được giữ lại. Lịch sử có thể chứa nội dung project; kiểm tra bản xuất trước khi chia sẻ.

| Biến môi trường | Công dụng |
|---|---|
| `MULTI_AGENT_HUB_DATA_DIR` | Thư mục dữ liệu riêng |
| `CODEX_BINARY`, `CLAUDE_BINARY`, `AGY_BINARY` | Đường dẫn CLI `.exe` |
| `ANTIGRAVITY_IDE_BINARY` | Antigravity IDE |
| `ANTIGRAVITY_BINARY` | Antigravity 2.0 desktop |
| `CODEX_HOME` | Cache/cấu hình Codex |

`--data-dir "D:\HubTestData"` mở profile thử nghiệm riêng.

## Phát triển và build

Yêu cầu Windows x64 và **.NET 10 SDK**. Không có NuGet dependencies bên ngoài.

```powershell
dotnet run --project src-desktop
dotnet run --project tests-native -c Release
./scripts/build-desktop.ps1 -Zip
# Chỉ build/publish, tự kiểm tra chức năng:
./scripts/build-desktop.ps1 -Zip -CreateShortcut -SkipTests
# Đóng gói mà không thay app đang chạy:
./scripts/build-desktop.ps1 -PackageOnly -SkipTests
# Double-click build-desktop.bat để build ZIP và tạo shortcut Desktop.
```

Build mặc định kiểm thử C#, publish vào staging rồi mới thay `app-bin/`; `-SkipTests` chỉ biên dịch/publish. Nếu app trong `app-bin/` đang mở, script yêu cầu đóng; không ép tắt tiến trình. Bản trước giữ ở `.build/previous-*`. ZIP là `release/MultiAgentHub-win-x64.zip`, chứa executable, icon, README, tài liệu và giấy phép. `.build/`, `bin/`, `obj/` là dữ liệu build tạm, có thể dọn khi bản thử nghiệm đã đóng.

- `src-core/`: catalog, cấu hình, CLI/API, Credential Manager, điều phối và xuất kết quả.
- `src-desktop/`: WinForms.
- `tests-native/`: kiểm thử bằng tiến trình fixture và HTTP giả lập, không cần tài khoản hoặc API có phí.
- `.github/workflows/build.yml`: kiểm thử và tạo ZIP trên Windows.

Harness hiện có kiểm tra adapter API bằng HTTP giả lập và các chế độ cũ. Bản 5.1 được giao theo yêu cầu chỉ kiểm tra biên dịch; không chạy harness hoặc kiểm tra màn hình/chức năng. Luồng nhóm và giao diện mới cần được người dùng kiểm tra thực tế. Kiểm tra live cần CLI đăng nhập hoặc key/model được tài khoản của bạn cấp quyền.

Phân loại lỗi quota tham khảo [OpenAI error codes](https://developers.openai.com/api/docs/guides/error-codes), [Claude API errors](https://platform.claude.com/docs/en/api/errors) và [Gemini API errors](https://ai.google.dev/gemini-api/docs/api-errors). Mã HTTP 429 riêng lẻ không đủ để kết luận hết quota.

## Đưa lên Git

`.gitignore` loại build, cache, môi trường, cấu hình và lịch sử riêng. Chỉ đưa source lên repo; chia sẻ ZIP qua Releases. Không đưa thư mục dữ liệu cá nhân hay key vào Git.

MIT License. Bản đóng gói có giấy phép .NET và thông báo thành phần bên thứ ba trong `licenses/`.
