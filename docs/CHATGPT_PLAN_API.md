# ChatGPT plan API — 5.13.0

Hub có kết nối **API chính thức dùng gói ChatGPT**, độc lập với extension, Codex CLI và app ChatGPT/Codex đang cài. Sau bước đăng nhập, yêu cầu chạy ngầm qua Responses API. Không cần giữ tab hay điều khiển cửa sổ. Đây không phải API điều khiển phần Chat trong Codex, không nhập lại lịch sử/cookie của ứng dụng đó.

## Dùng trong Hub

1. Mở **Agent & model → Tài khoản ChatGPT • API** (hoặc thêm công cụ **ChatGPT • API dùng gói đăng ký**).
2. Đặt tên để phân biệt tài khoản/workspace; bấm **Continue with ChatGPT**. Cho phép Hub dùng gói ChatGPT trong trang đăng nhập chính thức. Chưa cấp quyền thì Hub không chạy suy luận.
3. Hub lấy `/v1/models` của tài khoản, giữ thứ tự và tên/ID do máy chủ trả về. Chọn một model rồi **Thêm model vào Hub**. Không tạo hai agent mặc định, không suy đoán tên model từ bản EXE.
4. Ở Hub Chat, chọn agent và gửi chủ đề. Có thể cho agent API trao đổi với các agent khác theo chế độ hiện có. Nhãn **Using ChatGPT plan** hiện gần khung nhập khi dùng kết nối này.
5. Để dùng tài khoản/workspace khác, chọn **Thêm tài khoản / workspace khác…**. Để thay model của agent, mở thiết lập của agent và chọn lại tài khoản/model.

Lần đăng nhập đầu phải có thao tác đồng ý trong trình duyệt; việc này không phải điều khiển chatbot bằng giao diện. Từ lượt gọi tiếp theo Hub gửi trực tiếp `POST https://api.openai.com/v1/responses` với OAuth token và đọc SSE. Giao diện không báo hoàn tất cho tới khi nhận `response.completed`. Nếu luồng mất kết nối hoặc trả `response.failed`/`response.incomplete`, giữ phần văn bản đã nhận và báo lượt chưa xong; không tự gửi lại prompt.

## Model, quota và tính năng

Chỉ những model API do tài khoản trả về mới được chọn. Không cam kết danh sách/tính năng giống hoàn toàn Chat trên app/web: tính năng này có giới hạn preview riêng. Hub gửi văn bản và toàn bộ ngữ cảnh cần thiết của lượt, không dùng bộ nhớ/lịch sử ChatGPT có sẵn. Kết nối này chưa cung cấp tools hay nghiên cứu tự động bằng web search. Từ 5.13.0, ảnh/PDF được gửi trực tiếp, Word/văn bản được trích chữ; xem COMPLETION_AND_ATTACHMENTS.md.

Hạn mức áp dụng theo quyền và gói ChatGPT; các ứng dụng dùng chung gói có thể cùng tính vào hạn mức. Xem và quản lý quyền, hạn mức ứng dụng và lựa chọn dùng credits tại [ChatGPT Settings → Usage](https://chatgpt.com/settings/usage). Hub không cấu hình API key trả phí, không đổi sang tài khoản/model khác khi lỗi/quota. Các lựa chọn chi tiêu đã cấp cho ứng dụng tại ChatGPT Settings vẫn áp dụng.

## Lưu đăng nhập

Mỗi đăng ký tài khoản/workspace giữ client ID và danh tính đã xác minh riêng. Đăng nhập lại dùng client ID cũ; host ID ổn định là UUID ngẫu nhiên. Hub kiểm tra chữ ký RS256 theo JWKS OpenAI, issuer, audience, thời hạn và nonce trước khi lưu phiên mới. Token được mã hóa bằng Windows DPAPI CurrentUser, ghi nguyên bộ atomically vào thư mục `chatgpt-plan/` bên trong thư mục dữ liệu riêng của Hub. Không đưa token vào config agent, lịch sử, log hay Git. Không đọc credential của Codex hoặc trình duyệt.

Refresh dùng khóa file riêng từng tài khoản để tránh hai tiến trình dùng cùng refresh token xoay vòng. Đăng xuất cố thu hồi phiên rồi xóa token cục bộ; nếu chưa xác nhận thu hồi từ máy chủ, giao diện nêu rõ và dẫn tới ChatGPT Settings. Đăng xuất trong Hub không đăng xuất app Codex hay trình duyệt.

## Gemini desktop: chưa đáp ứng yêu cầu cùng quota app qua API

Việc có `Gemini.exe` trong máy không cung cấp API suy luận cho Hub. Qua kiểm tra bundle cài đặt được chỉ định, chưa tìm thấy giao thức API bên ngoài được công bố cho đúng phần chatbot đó. Google có Gemini Developer API, nhưng quyền/quota/billing của kết nối đó khác quyền dùng Gemini app/web; Hub không tự thay bằng API key hay Gemini Code Assist/Antigravity.

Do đó bản này **chỉ thêm đường ChatGPT plan API**; chưa hoàn thành yêu cầu hai chatbot ChatGPT và Gemini cùng chạy ngầm qua API với đúng quota của cả hai app. Extension/gateway web cũ vẫn là các kết nối thử nghiệm riêng, không được chuyển thành API desktop hay tự dùng làm fallback.

## Nguồn chính thức và kiểm tra

- [Đăng ký và đăng nhập](https://developers.openai.com/siwc/token-sharing-open-source/sign-in)
- [Tài khoản, refresh và sử dụng gói](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions)
- [Danh sách model và Responses](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference)
- [Giới hạn preview](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations)
- [Quyền Google AI trong AI Studio và giới hạn khi dùng API bên ngoài](https://ai.google.dev/gemini-api/docs/google-ai-plans)

Build cục bộ và fixture offline trong CI kiểm tra xác minh JWT, parser SSE, lưu DPAPI và khóa refresh; không chạy đăng nhập/suy luận thật hoặc kiểm tra giao diện trên máy người dùng. Chỉ đăng nhập và gọi thành công với tài khoản thật mới xác nhận được quyền dùng dịch vụ của tài khoản đó.
