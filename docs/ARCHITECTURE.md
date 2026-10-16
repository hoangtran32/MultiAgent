# Kiến trúc Multi-Agent Hub

## Phân lớp

```
src-desktop/                WinForms: hội thoại, project, thiết lập, tài khoản
  Teams/                   Phân vai/giới hạn và bảng task/đầu ra
  Controls/                Nút/vector, MessageFeed/MessageCard, Markdown → RichEdit
src-core/
  Teams/TeamModels.cs       Cấu hình nhóm, task, artifact, bằng chứng
  Teams/TeamProtocol.cs     Hợp đồng đầu ra, kiểm tra schema và DAG task
  Teams/TeamEngine.cs       Bàn tròn ↔ thực hiện ↔ phân tích kiểm tra/review
  Teams/TeamWorkspace.cs    Khóa writer, fingerprint, đọc file và chạy lệnh tin cậy
  AgentConnections.cs      Kiểm tra metadata đăng nhập/kết nối, mở login chính thức
  JobRunner.cs             Vòng đời tác vụ, hủy, snapshot, quota, bổ sung yêu cầu
  WorkspacePolicy.cs       Hướng dẫn project theo quyền người dùng và loại kết nối
  ProcessTransport.cs      CLI invocation/parser; không shell nối chuỗi
  CliDiagnostics.cs        Phân loại thông báo headless permission/auth đã biết
  ApiTransport.cs          Gọi API qua key của người dùng
  Storage.cs               Config, history, checkpoint nguyên tử
  Conversations.cs         Topic, JSON của lượt trao đổi và TXT
tests-native/              Harness kiểm tra native hiện có
scripts/                   Build/publish/đóng gói
```

Bàn luận mặc định liên tục từ 5.3.4 (PauseSeconds=0); preset 8 giờ cũng không thêm nghỉ. RestoreDiscussionLimits giữ nhịp chạy hiện tại khi mở/dùng lại job cũ, chỉ lấy lại giới hạn lượt/thời gian. Các mode CLI/API còn lại chuyển bước sau kết quả mà không chèn timer nghỉ.

Bàn luận 5.3.3: DiscussionControl dùng tín hiệu đồng bộ để đánh thức thời gian nghỉ hoặc yêu cầu kết thúc sau lượt; không hủy token của lượt model. JobRunner nhận bổ sung và ghi checkpoint trong cùng khóa với snapshot engine. DiscussionTurn.Kind=user có ID ổn định, không tăng số lượt model; gửi vào prompt kế tiếp/tổng hợp và lưu trong export/ngữ cảnh tiếp tục. UI giữ composer nhập được, chỉ khóa gửi lúc tổng hợp/kết thúc; không tạo lượt ngoài ngân sách.

Chốt từng ý (5.4.0) dùng mode consensus qua Catalog.IsTeamMode và tái sử dụng writer lock, quota, kiểm tra, review, checkpoint của TeamEngine. IdeaConsensus khóa một TeamTask chưa xong; ProposalVersion và AgreedBy là trạng thái thực, không suy diễn từ văn bản. TaskAmendments tăng đúng một phiên bản và xóa phiếu cũ. TaskReview.Version phải trùng bản hiện tại; mọi thành viên khả dụng xác nhận trước ready. Khi sửa phạm vi qua bổ sung/tiếp tục, xác nhận hết hiệu lực. Ý sau chỉ tạo khi ý trước done/planned. Round ngắn, không idle-break sau ba vòng; ngân sách vẫn giới hạn. Lỗi thông thường ngăn việc giả quorum; quota có ghi nhận fallback đơn. Kế hoạch Chat dùng planned và không gọi work. Core tests trong ConsensusTests.cs dùng executor giả, không gọi model thật.

Từ 5.4.1, TaskBlocker ghi nguồn báo chặn, lý do, điều kiện tiếp tục và người kiểm chứng. TeamEngine chuyển blocked sang ReviewBlocker của đồng đội. revise tăng phiên bản/đặt lại quorum trước khi thử, tính vào MaxRevisions; blocked có requiredAction rõ ràng và không bao giờ chuyển done. JobRunner tiếp tục blocked bằng kiểm chứng trước, không gọi lại writer ngay. Bổ sung scope trong lúc kiểm chứng làm hết hiệu lực kết quả vừa nhận. Chat/bảng task/export trình bày vướng mắc riêng.

Từ 5.4.2, tiếp tục blocked chuyển sang proposed + NeedsScopeReview, giữ lý do trước để đối chiếu; không dùng lại kết luận blocked trước khi đọc yêu cầu mới. IdeaConsensus từ chối taskReviews cho đến khi taskAmendments cập nhật phạm vi rồi mới lấy quorum. TaskBlocker revise và bổ sung đến trong lúc kiểm chứng cũng bật NeedsScopeReview. Prompt đặt yêu cầu người dùng hiện hành cuối ngữ cảnh và ưu tiên phần bổ sung mới nhất khi rút gọn; kế hoạch/lời model cũ không được ghi đè quyền người dùng vừa cho. Các task done không bị mở lại khi tiếp tục phiên. Thẻ chat phân biệt kiểm chứng đang chạy và kết luận dừng.

Từ 5.4.3, CliParser.Completion chỉ phát sau terminal thành công có answer (Codex turn.completed, Claude result, agy result). ProcessTransport dùng Process.Exited để nhận root exit độc lập pipe EOF; đua root exit với Completion, cho 2 giây thoát rồi chỉ kill root nếu terminal đã thành công. Exit code do bước cleanup đó không thay kết quả; lỗi provider/định dạng vẫn bị từ chối. Cancel trước terminal vẫn hủy process tree; process nền sau terminal được giữ. Thu pipe bị giới hạn như trước. TeamEngine ghi timeout riêng và đưa tên agent/lỗi cụ thể vào StopReason. Task.PreviousAttempt giữ đầu ra Coder lỗi độc lập feedback để bỏ phiếu không ghi đè; resume nhập dữ liệu cũ từ turn và gửi lại ở bước thực hiện với nhãn chưa xác nhận. Chỉ xóa sau khi đã nhận câu trả lời thực hiện hợp lệ; không tự coi task done.

Từ 5.5.0, RunProgress tạo trạng thái trình bày từ snapshot và cờ live thực tế, không ước tính thời gian/khối lượng còn lại. SessionProgressView nằm ngoài MessageFeed, cập nhật label khi nội dung đổi; thời gian làm tròn theo giây. AutoSize row riêng không dùng chung row composer. Task count tách khỏi call budget; all-done không thay job.State/quorum. Lịch sử có state running nhưng không có runner live hiển thị gián đoạn. TeamBoard giữ các row cùng ID và lựa chọn thay vì clear mỗi lần đổi task state.

Từ 5.5.1, SessionProgressView nằm trong row riêng của ConversationSidebar, dùng GetPreferredSize/bounds theo DPI để tránh FlowLayout giãn hàng. Sidebar khóa từng control điều hướng thay vì khóa cả parent, giữ thao tác phiên hoạt động. MessageFeed có ActivityIndicator riêng sau các card, timer 120 ms chỉ Invalidate indicator; animation không thay text/chiều cao, dừng khi ẩn/dispose. Canvas nhận activeJobId từ runner thực để lịch sử không giả loading. ConversationTimeline lọc phần Directives kế thừa/đã có prompt hoặc user turn. AddTeamInstruction checkpoint user turn queued cùng pending directive; TeamEngine đánh dấu áp dụng tại ranh giới bước, số lượt AI không tính user turn.

Desktop phụ thuộc Core. Core không phụ thuộc WinForms. IAgentExecutor là ranh giới gọi model, cho phép thêm adapter mà không đưa logic nhà cung cấp vào bàn tròn. Chưa thêm framework bên ngoài.

Trong 5.3.0, HubConfig.Projects/ProjectSections lưu alias, pin, section và dấu Removed; ProjectCatalog merge RecentProjects/metadata hội thoại mà không khôi phục dấu gỡ. Thêm folder chủ động mới khôi phục. ArchiveProject chỉ đổi metadata Archived của hội thoại thuộc đúng đường dẫn; không ghi đè job/TXT hoặc xóa file project. Menu project phát sự kiện riêng cho MainWindow, không tự tác động filesystem.

OpenConversationAsync đọc stamp/job JSON trên worker, kiểm tra cancellation/revision trước khi bind; cache dữ liệu tối đa 4 topic theo stamp metadata. Canvas giữ LRU 3 MessageFeed native, mỗi feed giữ viewport/follow và các card theo ID. Khởi tạo 32 sections gần nhất, tải thêm 32 khi yêu cầu. Chuyển topic không gọi SetProject→NewConversation hoặc ReloadLibrary, không dựng control nhật ký ẩn; RenderDetails chỉ chạy khi mở Chi tiết hoặc khi dialog đó đang hiển thị. Đây là giảm I/O/control churn, chưa có đo hiệu năng hoặc UI test cục bộ.

Trong 5.2.0, AgentDiscovery trả kết quả tìm executable và ModelInventory có nguồn/giới hạn xác minh; không ghi HubConfig hoặc bật agent. Scan tối đa 3 truy vấn metadata đồng thời, timeout 30 giây/công cụ và hủy khi đóng dialog. API chỉ dò cấu hình người dùng đã khai báo. CLI dùng metadata/cache/config; không probe bằng inference. AgentEditor hủy yêu cầu cũ theo revision khi đổi kết nối, giữ bộ chọn/nút lưu hoạt động và không ghi đè model đang nhập bằng kết quả trễ. AgentDiscoveryDialog chỉ chuyển lựa chọn sang editor; Commit chỉ diễn ra sau Lưu. Provider trong Catalog là adapter protocol; danh sách runtime agent vẫn khởi tạo rỗng.

ProcessTransport 5.1.6 đợi tiến trình CLI thoát trước, sau đó thu nốt stdout/stderr tối đa 3 giây. Tiến trình con tồn tại lâu hơn (ví dụ ADB server) có thể kế thừa đầu ghi pipe và ngăn EOF dù CLI đã xong; xem [pipe inheritance của Windows](https://learn.microsoft.com/en-us/windows/win32/procthread/creating-a-child-process-with-redirected-input-and-output). Hub hủy đọc I/O, đợi thêm tối đa 1 giây, ghi log và kiểm tra exit code/parser bình thường; không giết dịch vụ nền để lấy EOF. Hủy/timeout trong khi CLI còn chạy vẫn dừng process tree như trước. Không suy ra hoàn tất chỉ từ một đoạn văn bản của model.

Từ 5.1.5, HubConfig khởi tạo danh sách agent rỗng; profile mới tự chọn provider/connection/model qua giao diện. Không có agent/tài khoản/model cá nhân kèm binary. Cấu hình native đang có được đọc nguyên trạng. Migration JS chỉ lấy definitions hoặc ID có thực trong defaultAgents/agentOptions cũ; các ID nhà cung cấp trong migration chỉ phục vụ tương thích lịch sử, không dùng để điều phối. Engine dùng danh sách agent đã chọn, hỗ trợ một kết nối hoặc nhóm tùy chọn, tối đa 32. Welcome và nút gửi dẫn đến quản lý agent khi danh sách trống; thêm agent cần chọn provider rõ ràng.

Giao diện 5.1 dùng palette sáng, ModernButton tự vẽ và menu lựa chọn bọc trạng thái ComboBox hiện có. Màn hình hội thoại không có TabControl; bảng task là panel phụ hoặc hộp riêng khi cửa sổ hẹp. Composer có hàng cố định, tự tăng chiều cao theo văn bản, không để AutoSize/Percent tạo khoảng trống. NativeMarkdown escape nội dung và chỉ cho mở HTTP(S) khi người dùng bấm. Không WebView/HTML/JavaScript. Màu và kích thước tính theo DPI tập trung trong UI, engine không đổi theo giao diện.

Trong 5.1.8, MessageFeed giữ MessageCard theo ID job/lượt/kết quả, tái sử dụng control khi streaming. Chỉ body đổi text mới gán RTF; log/metadata không làm dựng lại body hoặc cả hội thoại. Card người dùng chiếm 72% cột, đặt bên phải; mọi AI bên trái. Nội dung bên trong luôn căn trái, với khung màu/nút thật và đường phân cách. RichEdit báo chiều cao qua [ContentsResized](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.richtextbox.contentsresized) và EM_REQUESTRESIZE. Vùng cuộn dùng một viewport và anchor theo ID tin để giữ điểm đọc khi tin khác lớn lên; không tự cuộn về đầu control khi focus. Mở đầu tải 80 sections, tải thêm theo yêu cầu; preview mỗi body giới hạn 24.000 ký tự, nội dung đầy đủ giữ trong record và hộp Chi tiết. Không thay JSON/TXT hay điều phối.

Từ 5.1.11, MessageFeed kết thúc SuspendLayout bằng ResumeLayout(true) sau khi đặt bounds/AutoScrollMinSize, rồi mới đặt AutoScrollPosition; ScrollableControl cần layout này để cập nhật range native. Giới hạn cuộn lấy từ DisplayRectangle hiện tại, không từ extent chưa áp dụng. Card chuyển tiếp wheel về feed; gửi yêu cầu chủ động bật theo cuối, còn streaming giữ anchor khi người dùng đang đọc lịch sử.

SidebarTree bật [TVM_SETEXTENDEDSTYLE](https://learn.microsoft.com/en-us/windows/win32/controls/tvm-setextendedstyle) với TVS_EX_DOUBLEBUFFER. Hover chỉ invalidate dòng trước/sau, không invalidate toàn bộ cây. Hàng chọn Hub có chiều cao 48 logical px tính lại theo DPI, nút Dock.Fill không dùng AutoSize. ConversationTranscript.SetPlainText tạm dừng redraw, giữ selection/dòng đầu cho nhật ký, prompt và các tab hội thoại khi nội dung cập nhật.

Trong 5.1.1, ModernButton đo và vẽ bằng cùng `NoPadding`/`NoPrefix`, dùng graphics của control khi có handle; xem [TextFormatFlags](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.textformatflags). NavigationRail bố trí icon trực tiếp theo DPI, tránh FlowLayout tự tăng kích thước. SidebarTree dùng [TVS_NOHSCROLL](https://learn.microsoft.com/en-us/windows/win32/controls/tree-view-control-window-styles) để chỉ giữ cuộn dọc, vì nhãn dài đã được owner-draw với ellipsis.

Trong 5.1.2, ConversationTranscript bật thanh cuộn dọc native. Khi cập nhật RTF, giữ selection và dòng đầu đang hiển thị nếu người dùng đang đọc phần cũ; chỉ theo cuối khi người dùng cuộn về cuối hoặc bấm nút đến cuối. Dùng vị trí dòng thay cho pixel vì [EM_GETSCROLLPOS](https://learn.microsoft.com/en-us/windows/win32/controls/em-getscrollpos) giới hạn tọa độ 16 bit, dễ sai với hội thoại dài.

WorkspacePolicy được dùng chung cho thảo luận, nhóm và tác vụ thường. Từ 5.1.4, Hub Code luôn gửi project/cwd và giữ nguyên quyền đã cấu hình trong mọi lượt, không tự chuyển `full`/`workspace-write` về `read-only` khi thảo luận/review/sửa schema. Prompt mô tả đúng quyền CLI, không gắn nhãn mọi lệnh terminal là chỉ đọc. Vai trò vẫn xác định mục tiêu lượt; nhóm đề xuất thay đổi qua task và review đối chiếu workspace, không tự triển khai ý tưởng chưa được yêu cầu. Hub Chat cấm công cụ và dùng bản sao quyền đọc; API thuần văn bản không được mô tả là có filesystem tools. Không tự quét hoặc gửi toàn bộ thư mục qua API. Đầu ra cũ không được tự sửa khi cập nhật chính sách; gửi lượt mới trong cùng topic để áp dụng.

Trong 5.1.3, Antigravity ưu tiên công cụ file native ở quyền khác `full`, tránh dùng terminal chỉ để đọc. Headless có thể soft-deny công cụ, thoát 0 nhưng không có câu trả lời; AgentExecutor nhận diện thông báo stderr đã biết trong cả luồng thoát lỗi và luồng rỗng. Không tự coi rỗng là lỗi đăng nhập và không tự nâng quyền/retry. Quota vẫn được ưu tiên; lỗi có cấu trúc của parser được giữ khi process thoát lỗi. Parser tiêu thụ dòng cuối đúng một lần. Kiểm tra/Tải model Antigravity dùng `models`, không gọi `--version` không được CLI hiện tại hỗ trợ; metadata không chứng minh quyền công cụ hay quota suy luận.

## Bất biến điều phối

- Instructions riêng và cấu hình quyền toàn cục không bị thay đổi khi đổi vai; mỗi lần gọi dùng bản sao.
- Phân vai là chuyên môn và kết nối ưu tiên, không phải cấp bậc. Model đề xuất người phát biểu; bộ điều phối giữ cơ hội tham gia trong vòng và giới hạn ngân sách.
- Chỉ đầu ra JSON đã hợp lệ mới thay đổi task. ID trùng, thiếu tiêu chí, phụ thuộc chưa tồn tại hoặc vòng tròn bị từ chối. Văn xuôi không được dùng làm lệnh điều phối.
- Một lần thực hiện tại một thời điểm; phiên nhóm có khóa FileStream ngoài workspace theo hash đường dẫn. Khóa này không kiểm soát editor khác hoặc chế độ cũ của Hub.
- Model có thể đề xuất task, tài liệu, ghi nhận và phản biện; không thể tự thêm lệnh kiểm tra vào danh sách executable mà người dùng đã cấu hình.
- Kết quả lệnh chỉ có hiệu lực với fingerprint được ghi nhận. Task sửa code tiếp theo, thay đổi yêu cầu và tiếp tục phiên làm hết hiệu lực bằng chứng cũ.
- Fallback không tăng quyền hoặc giả lập khả năng sửa file cho API thuần văn bản. Quota bỏ chọn kết nối; lỗi khác loại kết nối khỏi phiên, giữ cấu hình đã chọn.
- Mọi lượt gọi, cả lỗi và sửa schema, tính vào ngân sách. Luồng sửa schema giữ quyền CLI đã chọn nhưng yêu cầu chỉ sửa định dạng JSON, không phát lại công cụ hoặc thao tác trước.
- Bổ sung từ người dùng vào hàng đợi, áp dụng tại ranh giới bước; không thay đổi request đang chạy.

## Lưu trữ và khôi phục

Giữ tương thích schema cũ bằng các trường mới có giá trị mặc định. `JobRecord.Team` chứa `TeamRun` schemaVersion=1; task/đầu ra/bằng chứng nằm cùng JSON của lượt trao đổi. `DiscussionTurn` bổ sung vai trò, task ID và đầu ra có cấu trúc để truy vết. Transcript TXT/Markdown là bản xuất, JSON là trạng thái máy đọc.

Checkpoint dùng cơ chế có sẵn trong Storage; không tạo cơ sở dữ liệu thứ hai. Phiên bị gián đoạn được đánh dấu interrupted khi đọc lại. Tiếp tục tạo JobRecord mới liên kết PreviousJobId, sao chép board/đầu ra, đặt lại task chưa xong để peer xác nhận; không phát lại lệnh tự động. Mỗi phiên tiếp tục có ngân sách mới. Không khôi phục terminal session của nhà cung cấp.

## Phát triển và build

Cần .NET 10 SDK trên Windows để phát triển; bản publish tự chứa runtime.

```powershell
dotnet build MultiAgentHub.slnx -c Release
./scripts/build-desktop.ps1 -Zip -CreateShortcut
```

Build script mặc định chạy harness hiện có. Nếu người dùng chủ động kiểm tra chức năng và chỉ muốn biên dịch/đóng gói:

```powershell
./scripts/build-desktop.ps1 -Zip -CreateShortcut -SkipTests
# ZIP mới, không thay app đang chạy:
./scripts/build-desktop.ps1 -PackageOnly -SkipTests
```

Không đặt credentials, dữ liệu hội thoại hoặc cache vào Git. `app-bin`, `release`, `.build`, `bin`, `obj` được bỏ qua. Cấu hình runtime cá nhân nằm trong LocalAppData; API key riêng dùng Windows Credential Manager. Mở rộng kết nối: thêm Catalog, adapter, khả năng đăng nhập và tài liệu; không đưa truy cập credential vào view.

`-PackageOnly` publish vào staging và tạo ZIP qua file tạm trước khi thay archive hoàn chỉnh, không thay app-bin hoặc shortcut. GitHub Actions dùng chế độ này với quyền `contents: read`; harness mặc định vẫn chạy trừ khi chọn `-SkipTests`. Staging/bin/obj là dữ liệu sinh có thể dọn sau build khi không có process chạy từ chúng.

## Phạm vi bản 5.0

Thực hiện bàn tròn có cấu trúc, task tuần tự, tài liệu, review, tùy chọn lệnh kiểm tra, quota fallback, tiếp tục và giao diện native. Chưa có nhiều coder/worktree, manager tự học, tool executor tổng quát cho API hoặc cơ chế phân tích coverage tự động. Harness cũ không được coi là kiểm chứng luồng nhóm mới; bản giao theo yêu cầu chỉ được kiểm tra biên dịch, người dùng tự test chức năng/giao diện.
