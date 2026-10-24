# Hub Code • Luồng IDE 5.15.0

## Cấu hình

Trong **Làm việc / Hub Code**, thêm các kết nối cần dùng ở Agent & model. Bấm **Phân công code • Đầu não / Thợ code**, bật luồng IDE và chọn agent/model riêng cho:

- **Đầu não:** đọc code, thiết kế thuật toán/cấu trúc dữ liệu và hướng triển khai; không tự sửa project ở lượt này.
- **Thợ code:** nhận hướng dẫn đã bàn giao, triển khai đúng task, xử lý sửa đổi sau review.

Model có thể khác model dùng khi thảo luận trên cùng kết nối, hoặc dùng kết nối khác. Dò model chỉ đọc metadata, không gọi suy luận. Không có cặp GPT/Gemini mặc định. Ví dụ chọn model GPT cho hướng thuật toán và model Gemini cho triển khai chỉ hoạt động khi các kết nối/model đó khả dụng và Thợ code có công cụ ghi thật.

Khi bật, Hub chọn **Chốt từng ý** và bật thực hiện task. Hai agent được chọn tham gia phiên; các thành viên đang chọn khác tiếp tục tham gia thảo luận/review. Thợ code cần CLI hoặc API có harness, quyền workspace-write/full. Extension/gateway web chưa có công cụ ghi project. Không tăng quyền tự động. Các phân vai thảo luận/reviewer và lệnh kiểm tra vẫn cấu hình tại **Trao đổi & giới hạn / phân vai**.

## Thứ tự thực hiện

1. Nhóm đề xuất, phản hồi và chốt đúng phiên bản từng ý. Các ý được chốt lưu `planned`; chưa code. Ý tiếp theo có thể phụ thuộc ý đã chốt.
2. Khi các thành viên đều cho rằng kế hoạch đã bao phủ yêu cầu, mọi thành viên review **toàn bộ giải pháp**. Chỉ khi mọi bên thông qua cùng yêu cầu hiện hành, Hub lưu hash giải pháp và chuyển sang triển khai.
3. Với từng task theo thứ tự phụ thuộc, model **Đầu não** đọc code hiện tại rồi trả hướng thuật toán, điểm cần thay và cách kiểm tra. Bản bàn giao gắn task version, model thực tế và phiên bản phân công.
4. Model **Thợ code** nhận bản bàn giao, sửa phần được giao; không làm các phần ngoài kế hoạch.
5. Hub chạy lệnh kiểm tra đã cấu hình nếu bật; đồng đội review kết quả. Revise gửi về Đầu não/Thợ code để sửa phần cần thiết, không tự tạo vấn đề mới. Sau đó xử lý task tiếp theo.
6. Đối chiếu kết quả với yêu cầu gốc trước khi kết thúc. Review kế hoạch không là bằng chứng code/test. Tự kiểm tra hoặc tự review vẫn có trạng thái chưa kiểm chứng như trước.

Với các CLI hỗ trợ quyền, bước chốt/review kế hoạch và Đầu não chạy read-only; API harness không cấp công cụ ghi/lệnh ở các bước này. CLI tùy chỉnh vẫn tự quản lý thực thi của nó. Không có sandbox OS mới.

## Đổi vai hoặc yêu cầu

Có thể mở lại **Phân công code** khi phiên IDE đang chạy. Trong phiên chỉ chọn trong các agent đã tham gia; thêm agent mới trước phiên sau. Phân công mới được lưu cho phiên sau và xếp hàng áp dụng ở ranh giới bước hiện tại.

Nếu phân công thay đổi khi Đầu não đang trả lời, bản bàn giao cũ chưa chạy bị bỏ và model mới thiết kế lại. Nếu Thợ code đã bắt đầu thao tác, không ngắt/hoàn tác để đổi vai; task đã nghiệm thu không tự làm lại. Các task chưa thực hiện nhận phân công mới.

Bổ sung phạm vi mới đưa phần chưa xong về chốt và review giải pháp; kết quả cũ đã nghiệm thu được giữ. Lỗi/quota của một vai hiện tên/model/lý do thật; không tự thay bằng model khác hoặc giả đồng thuận. Tiếp tục từ lịch sử lấy cài đặt hiện hành, đối chiếu file/thay đổi trước khi xử lý phần còn lại.

## Workspace

Nút **Workspace IDE** ở đầu Hub Code mở khu làm việc gồm:

- **Giải pháp & task:** phạm vi, xác nhận, đầu não/thợ code thực tế, bản bàn giao và bằng chứng.
- **File & editor:** cây project tải theo thư mục, mở file bằng nhấp đúp; hỗ trợ text tối đa 1 MB, Ctrl+S/lưu. Giữ encoding/BOM. Hash khác lúc mở thì không ghi đè; giữ bản nháp để đối chiếu/tải lại. Không lưu trong khi agent chạy. Khi đổi project có bản nháp, sao chép hoặc đóng file trước khi mở project mới.
- **Git diff:** diff của file đang chọn so với HEAD, không chạy external diff/textconv; file mới chưa theo dõi có thể chưa xuất hiện. Xem bản đã lưu, không phải bản nháp editor.
- **Nhật ký lệnh / agent:** tail nhật ký và kết quả kiểm tra; đây là trình xem, chưa phải terminal tương tác hay debugger.

Workspace chặn đường dẫn thoát project, liên kết và `.git`; không tự mở chương trình ngoài. Cửa sổ hẹp mở workspace riêng để có đủ chỗ; cửa sổ rộng có thể hiện bên cạnh chat. Đây là editor/diff/bảng agent native, chưa có LSP, debugger hoặc bộ extension IDE.

## Kiểm chứng

Fixture CI dùng executor/model giả và lệnh executable fixture, không gọi tài khoản thật. Kiểm tra không code trước review toàn bộ, không dùng model sai vai, thay phân công giữa lượt, yêu cầu mới, lỗi và resume, cùng bảo toàn nội dung/encoding khi lưu. Chưa kiểm tra UI hoặc suy luận live tại máy người dùng.
