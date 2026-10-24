# Harness dùng chung — 5.14.0

Hub sở hữu vòng lặp công cụ và trạng thái cho các kết nối API. Model chọn công cụ qua function calling native; Hub kiểm tra tên/đối số/quyền, ghi receipt, thực thi, rồi gửi kết quả về đúng cuộc trao đổi. Câu trả lời cuối đi tiếp vào DiscussionEngine/TeamEngine như trước: trả lời xong một lượt không tự chứng minh toàn bộ task đã nghiệm thu.

## Sử dụng

1. **Agent & model → Runtime & công cụ**: bật harness, chọn web/lệnh, ngân sách và thời gian lệnh.
2. **Thiết lập / model**: chọn model do kết nối cung cấp và quyền cho API. `default`/`read-only` chỉ đọc workspace; `workspace-write` cho sửa file; `full` thêm chạy lệnh khi đã bật.
3. Chat không cần project. Code dùng project hiện chọn. Gửi yêu cầu như bình thường; trạng thái bên trái hiện suy luận/công cụ đang chạy, số lần model và công cụ. Chi tiết agent có nhật ký tên công cụ và ID receipt.
4. Có thể gửi bổ sung khi đang chạy. Harness áp dụng ở ranh giới bước; nếu bổ sung đến khi đang suy luận, đề xuất công cụ cũ chưa chạy sẽ bị bỏ. Thao tác đã bắt đầu có thể có tác dụng trước khi dừng.

Ngân sách mặc định mỗi lượt agent: 24 lần model, 80 công cụ, 240.000 ký tự ngữ cảnh, 16.000 ký tự đầu ra công cụ, 120 giây/lệnh. Hai giới hạn lượt đều có thể đặt **0 = không giới hạn**. Ngân sách nhóm vẫn tính các request bên trong, không chỉ lời trả của agent. Giới hạn là điểm dừng **chưa xong**, không là bằng chứng hoàn tất. UI đếm ký tự, không giả là số token chính xác.

## Khả năng theo không gian

| Công cụ | Chat | Code |
| --- | --- | --- |
| Đọc chữ tệp đính kèm, ảnh/PDF native | Có, theo khả năng model | Có |
| Bộ nhớ hội thoại, kế hoạch và nhật ký | Có | Có |
| Artifact `.txt/.md/.csv/.json/.html/.svg` | Thư mục riêng của hội thoại | Thư mục riêng của hội thoại |
| Đọc URL HTTPS, tìm kiếm đã cấu hình | Có, nếu bật web | Có, nếu bật web |
| Đọc/tìm file project, thông tin môi trường | Không | Có |
| Ghi file project | Không | Quyền ghi/full, bước thực hiện |
| Chạy executable và đối số | Không | Full + bật lệnh, bước thực hiện |

API trong nhóm chỉ có quyền ghi/lệnh ở bước work được giao; bàn tròn, review, đối chiếu điểm dừng và sửa schema chỉ đọc. CLI giữ chính sách của CLI và writer lock cũ. Quyền ghi API không được suy ra từ lời model hay bộ nhớ. API không thể tự đổi model/tài khoản, bật công cụ hoặc tăng quyền.

`write_file` yêu cầu SHA256 từ nội dung đã đọc hoặc `new` cho file mới. Chặn đường dẫn thoát project, liên kết filesystem, alternate data stream và `.git`; file tối đa 1 MB. Giữ khóa writer phối hợp giữa các phiên Hub có ghi. Hash/khóa không phải sandbox hệ điều hành và không ngăn mọi editor ngoài thay file. Không có công cụ xóa file.

`run_command` chạy trực tiếp executable + mảng đối số, thư mục hiện tại là project. Shell phải là executable được yêu cầu rõ trong lời gọi. `full` không có sandbox OS; lệnh có thể tác động ngoài project. Hủy/timeout dừng cây tiến trình của lệnh. Kết quả có state, exit code và log; thoát 0 vẫn cần được đối chiếu tiêu chí task.

## Nghiên cứu và bộ nhớ

`web_read` chỉ lấy HTTPS công khai, không cookie/header tài khoản, không proxy hệ thống, không localhost/LAN/cổng tùy ý; DNS được kiểm tra trên kết nối thực và từng redirect. Tối đa 2 MB/trang. Trả URL và thời gian lấy để trích nguồn. HTML được chuyển thành chữ, không thực thi script. Không dùng công cụ này cho trang cần đăng nhập/challenge.

`web_search` xuất hiện khi người dùng cấu hình URL HTTPS có `{query}`; Hub thay query đã URL-encode và đọc kết quả. Không mặc định thêm nhà cung cấp tìm kiếm, key, model hay billing. Chưa cấu hình thì chỉ có đọc URL, không tuyên bố đã tìm kiếm Internet. Website trả challenge hoặc nội dung không đọc được gây lỗi rõ.

Bộ nhớ chỉ chung trong cùng ConversationId; hội thoại khác không được đọc. Giới hạn 100 ghi chú/64.000 ký tự, mỗi ghi chú 8.000 ký tự. Ghi chú/file/trang web là dữ liệu, không thay quyền hay yêu cầu. Root `AGENTS.md` được nạp trong Code, hướng dẫn cho file ở thư mục con kèm khi đọc file. Không nạp hướng dẫn từ thư mục ngoài project.

Artifact văn bản nằm trong profile, không mở bằng app ngoài tự động, không ghi đè artifact trước. PDF/Word binary và ảnh đầu ra chưa được sinh bằng công cụ artifact này; CLI có thể dùng công cụ đã cài để tạo đầu ra trong Code. Đính kèm đầu vào vẫn theo hướng dẫn 5.13.0.

## Ngữ cảnh, nhật ký và khôi phục

Mỗi lượt agent có RunId. Profile `harness/<ConversationId>/<RunId>/state.json` lưu hoạt động/số lần gọi/kế hoạch/artifact. `call-N.json` ghi pending **trước** thao tác, ghi kết quả **sau** thao tác. Nếu ứng dụng dừng ở giữa, pending là kết quả chưa biết; không coi chưa chạy hoặc tự phát lại. `list_runs`/`read_trace` chỉ đọc cùng hội thoại. Bản ghi đang chạy của tiến trình đã chết được trình bày là interrupted.

Context giữ yêu cầu đầy đủ và nhóm call/result gần nhất nguyên cặp. Nhóm cũ được rút khỏi request với thông báo receipt để đọc lại; không đưa kết quả cũ thành lời khẳng định thành công. Nếu yêu cầu/tài liệu hoặc nhóm gần nhất vẫn quá lớn, dừng rõ và giữ tiến độ, không âm thầm cắt yêu cầu. Nhật ký receipt giữ phần chữ đầy đủ trong giới hạn công cụ; đầu ra lớn đưa preview vào model kèm thông tin rút gọn.

Nhật ký, bộ nhớ và artifact là dữ liệu riêng, có thể chứa nội dung file/lệnh; không đưa thư mục profile vào Git. Đăng xuất model không tự xóa hội thoại/nhật ký. Hủy tác vụ không hoàn tác side effect đã xảy ra.

## Adapter và mở rộng

`IHarnessModel` là ranh giới model, `HarnessTool` là schema, `HarnessTools` là thực thi theo policy, `AgentHarness` là vòng lặp, `HarnessJournal` là biên nhận. Thêm adapter không thay engine điều phối. Model phải hỗ trợ function calling; API không hỗ trợ sẽ báo lỗi, người dùng có thể tắt harness để dùng văn bản. Không fallback sang key tính phí hoặc model khác.

Responses giữ các output item cần continuation, bao gồm encrypted reasoning; Gemini giữ content/thought signatures; Anthropic giữ các block native. Chỉ tool call từ lượt đã kết thúc hợp lệ mới chạy. Call ID trùng hoặc output bị cắt không được thực thi. Function calls chạy tuần tự; không thêm writer chạy song song vào workspace.

CLI dùng harness của CLI; extension/gateway/manual giữ khả năng cũ, chưa nhận local tools từ harness API. Không có server MCP tổng quát, runtime JavaScript sandbox, computer use hoặc dịch vụ tìm kiếm/ảnh riêng trong bản này. Đây là lớp runtime của Hub, không phải bản sao toàn bộ hạ tầng ChatGPT.

Đối chiếu protocol: [OpenAI function calling](https://developers.openai.com/api/docs/guides/function-calling), [Codex harness](https://developers.openai.com/blog/codex-as-a-platform), [Gemini function calling](https://ai.google.dev/gemini-api/docs/function-calling), [Anthropic tool definitions](https://platform.claude.com/docs/en/agents-and-tools/tool-use/define-tools).

## Kiểm chứng

Fixture offline CI dùng model/HTTP giả, tệp tạm và executable fixture; kiểm tra quyền, nội dung file thật, chống stale write, receipt/cancel, ngữ cảnh, yêu cầu bổ sung, provider payload, opaque reasoning/signature và tích hợp JobRunner hai không gian. Build cục bộ không gọi model, mở đăng nhập hay kiểm tra UI trên tài khoản thật.
