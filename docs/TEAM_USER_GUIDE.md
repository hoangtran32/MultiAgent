# Dùng nhóm AI trong Hub 5.1

Giao diện 5.8.2: **Hub Code / Hub Chat** ở đầu thanh bên là Làm việc / Trò chuyện. Chọn **Bàn tròn** trong ô nhập, mở **Agent & model → Trao đổi & giới hạn / phân vai…** ở cuối sidebar. Rê chuột trên cách phối hợp hoặc từng mục menu để đọc giải thích. **Công việc & đầu ra** mở bằng biểu tượng danh sách ở góc trên phải. **Chi tiết**, TXT và quản lý topic nằm trong menu **⋯**. Các chỉ dẫn tên chế độ đầy đủ bên dưới vẫn chỉ cùng chức năng, không đổi dữ liệu hội thoại hoặc phân vai.

## Bắt đầu

1. Chọn project ở thanh bên. Mở **Agent & model**, bật ít nhất một CLI/API; hai kết nối khác nhau cho phép phản biện chéo.
2. Vào **Tài khoản / kết nối → Kiểm tra tất cả**. Kiểm tra này xác nhận trạng thái đăng nhập hoặc danh sách model, không bảo đảm quota còn đủ. Nếu thiếu đăng nhập, chọn agent rồi **Đăng nhập agent đã chọn**; hoàn thành luồng chính thức ở CLI/trình duyệt và kiểm tra lại. API nhập key ở Thiết lập / model. Kết nối IDE/desktop tự đăng nhập trong ứng dụng ngoài.
3. Chọn **Làm việc**, rồi **Bàn tròn & thực hiện**. Mở **Agent & model → Trao đổi & giới hạn / phân vai…** để phân vai, chọn model riêng và giới hạn gọi. Không cần tạo thêm kết nối chỉ để có thêm vai.
4. Nếu muốn model sửa code, cấp `workspace-write` cho CLI đó. Mặc định không tự tăng quyền. Để các API làm phân tích/review cũng được.
5. Nhập mục tiêu, phạm vi và điều kiện bạn sẽ dùng để nghiệm thu. Gửi bằng Ctrl+Enter hoặc Bắt đầu.

Từ 5.1.4, Hub Code giữ nguyên quyền đã chọn trong mọi lượt, gồm bàn tròn và Tester/Reviewer. Toàn quyền cho phép dùng file/terminal theo yêu cầu; Hub không tự ép về chỉ đọc. Vai trò quyết định công việc đang làm: đề xuất/đối chiếu ở bàn tròn và review, thay đổi project qua task thực hiện. Hub Chat vẫn chỉ trao đổi văn bản.

## Cách nhóm làm việc

Mỗi vòng bàn tròn cho thành viên khả dụng cơ hội phát biểu. Model đề xuất người tiếp theo; Hub chặn chọn thành viên đã phát biểu trong vòng. Một kết nối có nhiều chuyên môn được luân phiên góc nhìn. Đề xuất task phải có ID, mô tả, tiêu chí và phụ thuộc hợp lệ. Thành viên khác chấp nhận task trước khi thực hiện; một kết nối còn lại có thể tự chấp nhận ở lượt sau.

Task đủ điều kiện được thực hiện riêng; tiếp theo là kiểm tra đã cấu hình, phân tích của Tester và Reviewer. Kết quả trở lại bàn tròn để nhóm điều chỉnh hoặc bổ sung công việc. PM/Architect có thể phản biện Coder và ngược lại; không có model quản lý được quyền quyết định mọi việc. Task chạy tuần tự để tránh cùng ghi file.

## Xem và điều chỉnh

- **Hội thoại**: xem từng đóng góp, model, chuyên môn, task và lỗi.
- **Công việc & đầu ra**: chọn task để xem phạm vi, tiêu chí, kết quả và phản biện; mở tài liệu và bằng chứng lệnh ở các tab dưới.
- **Xuất tài liệu…**: tạo thư mục riêng chứa Markdown của phiên và từng tài liệu. TXT toàn bộ hội thoại vẫn tự cập nhật như trước.
- **Bổ sung**: gửi yêu cầu mới lúc nhóm đang chạy; áp dụng ở ranh giới bước. Task đã review sẽ cần xác nhận lại khi yêu cầu thay đổi. Bổ sung vừa gửi có thể còn trong hàng đợi cho đến ranh giới đó; không sửa prompt đã gửi đi.
- **Dừng sau bước**: giữ phần đang làm hoàn tất rồi kết thúc phiên. **Dừng**: hủy ngay, thay đổi code một phần vẫn có thể còn trong project.
- **Tiếp tục**: mở topic cũ và gửi yêu cầu mới. Hub giữ bảng task/đầu ra, đưa task chưa xong trở lại đề xuất để rà soát; ngân sách gọi và vòng sửa được tính lại cho phiên tiếp theo. Quota không tự bật lại.

## Kiểm tra và trạng thái

Mặc định người dùng tự kiểm tra. Kết quả code đã review vẫn hiện **Chưa kiểm chứng**. Muốn Hub chạy lệnh, vào Agent & model → Trao đổi & giới hạn / phân vai… → Lệnh kiểm tra, nhập executable và đối số JSON, ví dụ `dotnet.exe` với `["build"]`, rồi bật tự kiểm tra. Phải cấu hình lại hoặc xác nhận lại khi đổi project; không dùng câu lệnh do model sinh để tự chạy qua shell.

**Lệnh đạt** nghĩa là process trả exit code 0. Đọc log để biết có thực sự chạy test, có bỏ qua ca hoặc thiếu dependency không. Fingerprint xét file ngoài `.git`, `bin`, `obj`, `node_modules`, `.build`, `app-bin`, `release`, `dist`, `build`, `.vs`, `.idea`, `.next`, `coverage`, `__pycache__`, `.venv`, `venv`; source nằm trong thư mục bị loại trừ không được fingerprint bao phủ. Workspace quá 12.000 file/256 MB hoặc có liên kết source có thể không xác minh được.

**Đã review** là trạng thái task, không phải tuyên bố toàn bộ phần mềm đúng. **Chưa kiểm chứng** cũng được dùng khi chỉ còn một kết nối tự review hoặc chưa có thống nhất cuối. **Cần can thiệp**: xem lỗi cụ thể, sửa quyền/model/môi trường hoặc bổ sung yêu cầu rồi tiếp tục. Schema đầu ra sai được yêu cầu sửa một lần, không tự lặp vô hạn.

Giữ Hub mở và máy thức khi chạy dài. App mất điện/đóng bất ngờ giữ các bước đã lưu; nội dung đang stream có thể chưa lưu. Khi mở lại không tự chạy model hoặc lệnh. Bản 5.0 không tự commit, push, triển khai hoặc hợp nhất nhiều worktree.
