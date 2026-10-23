# Điểm dừng và đính kèm — 5.13.0

## Điểm dừng

Phạm vi chỉ gồm yêu cầu trực tiếp của người dùng và các bổ sung trực tiếp của họ. Đề xuất chatbot không mở rộng mục tiêu. Chỉ tiếp tục với việc bắt buộc còn thiếu hoặc lỗi liên quan tiêu chí; nâng cấp tùy chọn chỉ ghi thành gợi ý.

Trong Chat/Bàn luận, mỗi thành viên báo `complete` khi yêu cầu đã đủ, `continue` khi còn phần cần thiết chưa xong, `wait` khi thật sự cần thông tin từ người dùng. Cùng xác nhận hoàn tất trên cùng phiên bản yêu cầu thì Hub kết thúc, hiện **Đã hoàn thành yêu cầu**, giải phóng phiên và không gọi thêm lượt tổng hợp. Tin mới trong lúc trả lời vô hiệu hóa xác nhận cũ. Lỗi/quota không được coi là đồng thuận. Footer thiếu lặp lại được dừng rõ là lỗi điều phối ở phiên không giới hạn.

Trong Nhóm/Chốt từng ý, sau khi các task đã chốt xong có bước đối chiếu độc lập với yêu cầu. Bước này không tạo task/sửa tiêu chí. Nếu thật sự thiếu việc bắt buộc, phải dẫn nguyên văn yêu cầu người dùng và nêu phần chưa đạt. Khi vẫn bất đồng sau lần đối chiếu lại, Hub dừng **chưa kiểm chứng**, giữ kết quả; không tự mở rộng hoặc giả hoàn tất. Task đã xong không tự mở lại chỉ vì một tin nhắn bổ sung.

Xác nhận model không thay thế bằng chứng kiểm tra thực tế. Quyền, review/verification và ngân sách do người dùng cấu hình vẫn áp dụng; hết lượt/thời gian không chứng minh đã đáp ứng yêu cầu.

## Thêm tệp

Bấm **+ → Thêm tệp**, chọn nhiều tệp; hoặc kéo thả vào ô nhập. **Ctrl+V** dán ảnh/tệp từ clipboard. Danh sách dưới ô nhập có **×** để bỏ từng tệp; menu + có bỏ tất cả. Bấm tên tệp để xem dung lượng và giới hạn đọc.

Nhập yêu cầu rồi gửi. Nếu chỉ gửi tệp, yêu cầu mặc định là đọc và tóm tắt ngắn. Danh sách đang hiện là tệp gửi cùng lượt tiếp theo, được giữ để hỏi tiếp; bỏ khi không cần. Hội thoại mới xóa lựa chọn, không xóa tệp nguồn/bản lưu.

Giới hạn: **10 tệp**, **10 MB mỗi tệp**, **20 MB tổng**, **120.000 ký tự trích xuất**; văn bản tối đa 1 MB, PDF tối đa 200 trang. Không âm thầm cắt tài liệu vượt giới hạn. Đọc tài liệu chạy nền; không gửi khi đang chuẩn bị tệp. Giới hạn riêng của nhà cung cấp/model vẫn áp dụng.

| Tệp | Cách nhận |
| --- | --- |
| PNG/JPEG/WebP | Dữ liệu ảnh thật qua API hỗ trợ ảnh. |
| PDF | API nhận PDF gốc. CLI/extension nhận chữ trích xuất, không giả đã đọc hình/biểu đồ. |
| PDF quét/ảnh | Cần API nhận PDF trực tiếp; chưa OCR trên máy. |
| Word `.docx` | Trích chữ, đoạn/bảng; không lấy hình/biểu đồ nhúng. `.doc`/`.docm` cần lưu lại thành `.docx`. |
| Văn bản/code | UTF-8 hoặc Unicode có BOM. Từ chối định dạng nhị phân. |

Ảnh/PDF gốc được hỗ trợ qua **ChatGPT plan API, Chat Completions, Gemini Developer API, Anthropic API** khi model/endpoint cho phép. Hub không thêm agent mặc định, tạo key hoặc đổi tài khoản/billing. Gemini Developer API vẫn khác quota Gemini app/web. API tương thích có thể không hỗ trợ mọi loại file.

Nếu một agent được chọn chưa nhận được ảnh/PDF quét, Hub chặn cả lượt trước khi gửi. PDF/Word có chữ vẫn dùng được qua các kết nối văn bản/extension bằng phần chữ trích xuất. Tệp là dữ liệu tham khảo, không thay thế chỉ dẫn/phạm vi người dùng.

## Lịch sử

Khi gửi, byte gốc và chữ trích xuất lưu riêng trong `attachments/` ở thư mục dữ liệu Hub. JSON chỉ lưu tên, loại, dung lượng, hash. Mở lại hội thoại nạp tệp của lượt cuối từ bản lưu, không phụ thuộc tệp nguồn còn tồn tại. Bản lưu thiếu/thay đổi sẽ báo để đính kèm lại. Không sửa/xóa tệp nguồn, không tự mở bằng ứng dụng ngoài.

Fixture offline trong CI kiểm tra điểm dừng, tin mới, ngoài phạm vi, trích tài liệu, payload API và bản lưu. Không chạy suy luận/kiểm tra màn hình tại máy người dùng.
