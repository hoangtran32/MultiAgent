# Chốt từng ý

Chọn **Chốt từng ý** dưới ô nhập, bật các agent CLI/API muốn tham gia rồi gửi task. Hub chỉ giữ một ý đang xử lý:

1. A đề xuất một ý nhỏ cùng phạm vi và tiêu chí. Đề xuất là xác nhận của A cho phiên bản đó.
2. B phản hồi ngay ý này: đồng ý, phản đối có lý do hoặc sửa phương án.
3. Nếu sửa, Hub tăng phiên bản và bỏ mọi xác nhận cũ. A và B tiếp tục trên chính ý này đến khi cùng xác nhận phiên bản hiện tại.
4. Trong Hub Code, khi bật thực hiện, một agent xử lý phần đã chốt; đồng đội kiểm tra/review. Nếu cần sửa, quay lại phần đó.
5. Sau khi phần này hoàn tất, mới mở ý tiếp. Kết thúc khi các phần đã xong và tất cả thành viên khả dụng xác nhận yêu cầu đã được đáp ứng.

Ví dụ: thống nhất cách phát hiện mất mạng → triển khai và review phần phát hiện → thống nhất cơ chế khôi phục → triển khai và review khôi phục. Không yêu cầu một agent làm toàn bộ project trước rồi mới cho bên kia góp ý.

Mỗi lượt bàn ý chỉ khoảng 100–200 từ. Các lượt chuyển tiếp tự động, không chèn thời gian nghỉ và không cần bạn gửi tin giữa các lượt. Một lần gọi model vẫn cần hoàn tất câu trả lời của ý nhỏ đó; Hub không truyền tin chen ngang vào lần gọi đang sinh.

## Giới hạn và trạng thái

**Agent & model → Phân vai & giới hạn nhóm…** đặt số lần gọi, thời gian, số ý/task, vòng sửa, model cho từng chuyên môn và lệnh kiểm tra tùy chọn. Chốt từng ý dùng chung các giới hạn này với Bàn tròn. Mọi lần gọi, gồm sửa định dạng, đều tính vào ngân sách. Nhiều ý/phản biện có thể cần tăng giới hạn mặc định 30 lần gọi. Hết giới hạn thì lưu ý chưa chốt; không ép đồng ý hoặc tự mở ý khác.

**Công việc & đầu ra** hiển thị `v2 · 1/2`: phương án phiên bản 2 mới có một trong hai thành viên khả dụng xác nhận. Chọn ý để xem ai đã chốt, phạm vi, tiêu chí, kết quả và phản biện. Xác nhận là ý kiến của model, không phải bằng chứng code đã đúng; kết quả sửa code vẫn cần kiểm chứng.

Trong **Hub Chat** hoặc khi tắt thực hiện, ý được chốt thành **Đã lưu kế hoạch** rồi trao đổi ý kế tiếp; không chạy bước sửa workspace. API thuần văn bản có thể phân tích, chốt và tạo tài liệu; sửa file cần agent CLI có quyền đã cấp. Các chuyên môn ngang hàng, không phải một chuỗi chức danh cố định.

## Can thiệp và tiếp tục

Nếu agent thực hiện báo **bị chặn**, Hub chuyển cho đồng đội kiểm chứng trước khi kết thúc. Có cách xử lý trong phạm vi thì hai bên chốt lại phiên bản mới rồi tiếp tục chính ý đó, trong số vòng sửa đã đặt. Xác nhận vướng mắc không phải nghiệm thu task.

Nếu đồng đội xác nhận thiếu điều kiện bên ngoài, Hub hiển thị thẻ **Chưa thể tiếp tục** với lý do và điều kiện/thao tác cần làm. Ví dụ: backend đang chạy không khớp bản cần đo, trong khi phạm vi không cho phép thay tiến trình. Hub giữ task chưa hoàn tất, không tự tắt dịch vụ hoặc đổi điều kiện nghiệm thu để báo đạt. Sau khi điều kiện được đáp ứng hoặc bạn điều chỉnh phạm vi, gửi tiếp; Hub kiểm chứng vướng mắc trước khi thử thực hiện lại.

Gửi bổ sung trong lúc chạy để điều chỉnh yêu cầu ở ranh giới bước. Các xác nhận cũ bị làm hết hiệu lực; cần chốt lại phạm vi. **⋯ → Dừng sau bước hiện tại** kết thúc sau bước đang làm; nút Dừng hủy ngay. Kết quả và ý còn dang dở được lưu trong hội thoại, JSON, TXT và Markdown. Mở lại rồi gửi tiếp để tiếp tục; ý chưa hoàn tất phải xác nhận lại, không tự chạy khi mở app.

Chọn được một hoặc nhiều agent, không cố định Codex/Gemini. Với nhiều hơn hai agent, tất cả thành viên khả dụng phải chốt. Hết quota tự bỏ chọn theo cơ chế hiện có; chỉ còn một agent thì ghi rõ tự rà soát, chưa có đồng thuận/review độc lập. Lỗi thông thường sẽ dừng và giữ ý chưa chốt, không tự loại bên phản biện để biến thành đồng thuận. Kết nối dán kết quả thủ công chưa tham gia chế độ tự động này.
