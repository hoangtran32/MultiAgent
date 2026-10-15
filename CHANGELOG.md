# Changelog

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
