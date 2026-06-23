import os
import sys
import re
import ast
import json
import time
import subprocess
import copy
from collections import Counter

try:
    from dotenv import load_dotenv
    # Xác định thư mục gốc của dự án
    harness_dir = os.path.dirname(os.path.abspath(__file__))
    scripts_dir = os.path.dirname(harness_dir)
    root_dir = os.path.dirname(scripts_dir)
    env_path = os.path.join(root_dir, ".env")
    if os.path.exists(env_path):
        load_dotenv(env_path)
    else:
        load_dotenv()
except ImportError:
    pass

from harness.tools import (
    run_dotnet_command,
    read_source_file,
    write_source_file,
    search_codebase,
    patch_source,
    extract_compiler_errors,
    extract_test_errors,
    check_csharp_linting
)
from harness.safety import (
    is_path_safe,
    safe_parse_action_arguments,
    format_observation,
    selective_rollback
)
from harness.llm import (
    LLMRouter,
    build_cache_contents,
    generate_directory_tree,
    TOOLS_SCHEMA
)
from harness.memory import distill_and_persist_lessons

class AIDeveloperHarness:
    def __init__(self, auto_approve: bool = False):
        harness_dir = os.path.dirname(os.path.abspath(__file__))
        scripts_dir = os.path.dirname(harness_dir)
        root_dir = os.path.dirname(scripts_dir)
        
        # Đọc và dọn dẹp các API Key để tránh lỗi dấu nháy kép từ file .env
        gemini_key = (os.getenv("GEMINI_API_KEY") or "").strip("'\" \t")
        openai_key = (os.getenv("OPENAI_API_KEY") or "").strip("'\" \t")
        claude_key = (os.getenv("CLAUDE_API_KEY") or os.getenv("ANTHROPIC_API_KEY") or "").strip("'\" \t")
        deepseek_key = (os.getenv("DEEPSEEK_API_KEY") or "").strip("'\" \t")
        
        # Xác định Provider dựa trên khóa thực tế hợp lệ
        if gemini_key:
            self.provider = "gemini"
            self.api_key = gemini_key
        elif openai_key:
            self.provider = "openai"
            self.api_key = openai_key
        elif claude_key:
            self.provider = "claude"
            self.api_key = claude_key
        elif deepseek_key:
            self.provider = "deepseek"
            self.api_key = deepseek_key
        else:
            self.provider = "mock"
            self.api_key = None
            
        self.max_iterations = int(os.getenv("GAN_MAX_ITERATIONS") or 10)
        self.iteration_count = 0
        self.auto_approve = auto_approve
        self.pass_threshold = float(os.getenv("GAN_PASS_THRESHOLD") or 7.5)
        self.current_role = "Planner"
        
        # === LOOP ENGINEERING: Absolute Budget Ceiling (Improvement 1) ===
        # Tổng số iterations tối đa trên TOÀN BỘ pipeline (tất cả roles cộng lại).
        # Không bao giờ vượt quá giá trị này, kể cả khi budget được mở rộng động.
        self.absolute_max_iterations = int(os.getenv("HARNESS_ABSOLUTE_MAX_ITER") or 120)
        self.global_iteration_count = 0  # Đếm tổng iterations xuyên suốt pipeline
        
        # === LOOP ENGINEERING: Token Cost Tracking (Improvement 2) ===
        self.token_usage = {
            "total_input_tokens": 0,
            "total_output_tokens": 0,
            "total_cached_tokens": 0,
            "total_api_calls": 0,
            "estimated_cost_usd": 0.0,
            "per_role": {}  # {"Planner": {"input": N, "output": N, "calls": N}, ...}
        }
        # Pricing per 1M tokens (configurable via env, defaults for Gemini 2.5 Flash)
        self.pricing = {
            "input_per_1m": float(os.getenv("HARNESS_PRICE_INPUT_1M") or 0.15),
            "output_per_1m": float(os.getenv("HARNESS_PRICE_OUTPUT_1M") or 0.60),
            "cached_input_per_1m": float(os.getenv("HARNESS_PRICE_CACHED_1M") or 0.0375),
        }
        self.cost_ceiling_usd = float(os.getenv("HARNESS_COST_CEILING_USD") or 5.0)
        
        # Per-role iteration budgets (Fix 1: tránh agent loop vô tận)
        self.role_max_iterations = {
            "Planner": int(os.getenv("HARNESS_PLANNER_MAX_ITER") or (self.max_iterations if self.max_iterations == -1 else 8)),
            "TestWriter": int(os.getenv("HARNESS_TESTWRITER_MAX_ITER") or (self.max_iterations if self.max_iterations == -1 else 12)),
            "Developer": int(os.getenv("HARNESS_DEVELOPER_MAX_ITER") or (self.max_iterations if self.max_iterations == -1 else 40)),
            "Enricher": 3
        }
        
        # Cấu hình lưu trữ tệp tin log trong thư mục chuyên dụng (.claude/evals/)
        self.evals_dir = os.path.join(root_dir, ".claude", "evals")
        os.makedirs(self.evals_dir, exist_ok=True)
        self.log_file = os.path.join(self.evals_dir, "harness_run.log")
        
        # Ghi log dòng chào mừng cho phiên làm việc mới (Ghi đè 'w' để tránh log phình to)
        with open(self.log_file, "w", encoding="utf-8") as f:
            f.write(f"=============================================================\n")
            f.write(f"PHIÊN LÀM VIỆC MỚI KHỞI CHẠY (MULTI-AGENT SPEC-DRIVEN FLOW)\n")
            f.write(f"=============================================================\n")
        
        # Cấu hình mặc định (fallback)
        self.policy_files = ["CODING_POLICY.md", "CLAUDE.md"]
        self.skills_and_guides = []
        self.allowed_cmds = ["dotnet build", "dotnet test", "dotnet restore", "dotnet clean"]

        # Đọc cấu hình từ harness_config.json nếu có
        config_path = os.path.join(root_dir, "harness_config.json")
        if os.path.exists(config_path):
            try:
                with open(config_path, "r", encoding="utf-8") as cf:
                    config_data = json.load(cf)
                    self.policy_files = config_data.get("policy_files", self.policy_files)
                    self.skills_and_guides = config_data.get("skills_and_guides", self.skills_and_guides)
                    self.allowed_cmds = config_data.get("allowed_commands", self.allowed_cmds)
                print(f"⚙️ [Harness Control]: Đã nạp cấu hình từ {config_path}")
            except Exception as e:
                print(f"⚠️ [Harness Control]: Lỗi khi đọc file cấu hình, sử dụng mặc định: {e}")

        # Tự động nạp chính sách lập trình cục bộ
        self.policy_content = ""
        self.agents_rules = ""
        self.agents_md_path = os.path.join(root_dir, "AGENTS.md")
        try:
            for p_file in self.policy_files:
                p_path = os.path.join(root_dir, p_file)
                if os.path.exists(p_path):
                    with open(p_path, "r", encoding="utf-8") as pf:
                        self.policy_content = pf.read()
                    print(f"📖 [Harness Control]: Đang nạp chính sách lập trình từ '{p_file}'...")
                    break
        except Exception as e:
            print(f"⚠️ [Harness Control]: Lỗi khi quét/nạp chính sách lập trình cục bộ: {e}")

        # Tự động nạp các tài liệu kỹ năng (skills/guides)
        self.skills_contents = []
        for s_file in self.skills_and_guides:
            s_path = os.path.join(root_dir, s_file)
            if os.path.exists(s_path):
                try:
                    with open(s_path, "r", encoding="utf-8") as sf:
                        self.skills_contents.append((s_file, sf.read()))
                    print(f"📖 [Harness Control]: Đang nạp tài liệu kỹ năng từ '{s_file}'...")
                except Exception as e:
                    print(f"⚠️ [Harness Control]: Lỗi khi nạp tài liệu kỹ năng {s_file}: {e}")

        # Tự động nạp bài học tự động (harness_lessons.md) nếu có
        self.lessons_path = os.path.join(scripts_dir, "harness_lessons.md")
        self.lessons_content = ""
        try:
            if os.path.exists(self.lessons_path):
                with open(self.lessons_path, "r", encoding="utf-8") as lf:
                    self.lessons_content = lf.read()
                print(f"📖 [Harness Control]: Đang nạp bài học tự động từ '{os.path.relpath(self.lessons_path)}'...")
        except Exception as e:
            print(f"⚠️ [Harness Control]: Lỗi đọc lessons file: {e}")

        self.model_name = os.getenv("GEMINI_MODEL") or os.getenv("CLAUDE_MODEL") or os.getenv("OPENAI_MODEL") or os.getenv("DEEPSEEK_MODEL") or "gemini-2.5-flash"
        
        self.mock_mode = (self.provider == "mock" or not self.api_key)
        if self.mock_mode:
            print("[CẢNH BÁO]: Không tìm thấy khóa API của GEMINI, OPENAI, CLAUDE hoặc DEEPSEEK.")
            print("Harness sẽ chạy ở chế độ giả lập (Mock LLM) để minh họa quy trình.")
        else:
            print(f"[Harness]: Đã tìm thấy khóa API cho {self.provider.upper()}. Đang khởi tạo LLM Client...")
            
        self.llm_router = LLMRouter(
            provider=self.provider,
            api_key=self.api_key,
            model_name=self.model_name,
            log_file=self.log_file,
            mock_mode=self.mock_mode,
            on_token_usage=self.track_token_usage
        )

        self.modified_files = set()
        self.planner_production_files = []
        self.test_filter_keyword = "test"
        self.test_writer_files = []
        
        # === LOOP ENGINEERING: Structured Handoff Dict (Improvement 5) ===
        # Structured context truyền giữa các agent phases.
        # Mỗi phase populate fields của mình, phase sau đọc fields của phase trước.
        self.pipeline_context = {
            # Planner → TestWriter, Developer
            "plan_decisions": [],           # Key architectural decisions made
            "files_to_implement": [],       # Production files agent needs to create
            "stub_files_created": [],       # Skeleton files already created by Planner
            "risk_lane": "normal",          # Risk classification
            
            # TestWriter → Developer
            "test_files_created": [],       # Test files written
            "test_signatures": [],          # Method signatures tests expect
            "test_filter_keyword": "",      # Keyword for dotnet test --filter
            
            # Developer → Evaluator
            "production_files_written": [], # Files developer has written
            "last_build_status": None,      # True/False/None
            "last_test_summary": "",        # Summary of last test run
            
            # Cross-cutting
            "enriched_task": "",            # Enriched task description from Enricher
            "approved_plan": ""             # Full approved plan text
        }

    def _build_cache_helper(self):
        harness_dir = os.path.dirname(os.path.abspath(__file__))
        scripts_dir = os.path.dirname(harness_dir)
        root_dir = os.path.dirname(scripts_dir)
        return build_cache_contents(root_dir, self.policy_content, self.lessons_content, self.skills_contents, self.agents_rules)

    def track_token_usage(self, input_tokens: int, output_tokens: int, cached_tokens: int = 0, role: str = None):
        """=== LOOP ENGINEERING: Token Cost Tracking (Improvement 2) ===
        Tích lũy token usage và ước tính chi phí API xuyên suốt pipeline.
        """
        role = role or self.current_role
        
        # Accumulate totals
        self.token_usage["total_input_tokens"] += input_tokens
        self.token_usage["total_output_tokens"] += output_tokens
        self.token_usage["total_cached_tokens"] += cached_tokens
        self.token_usage["total_api_calls"] += 1
        
        # Per-role tracking
        if role not in self.token_usage["per_role"]:
            self.token_usage["per_role"][role] = {"input": 0, "output": 0, "cached": 0, "calls": 0}
        self.token_usage["per_role"][role]["input"] += input_tokens
        self.token_usage["per_role"][role]["output"] += output_tokens
        self.token_usage["per_role"][role]["cached"] += cached_tokens
        self.token_usage["per_role"][role]["calls"] += 1
        
        # Estimate cost
        fresh_input = input_tokens - cached_tokens
        cost = (
            (fresh_input / 1_000_000) * self.pricing["input_per_1m"]
            + (cached_tokens / 1_000_000) * self.pricing["cached_input_per_1m"]
            + (output_tokens / 1_000_000) * self.pricing["output_per_1m"]
        )
        self.token_usage["estimated_cost_usd"] += cost
        
        # Cost ceiling warning
        total_cost = self.token_usage["estimated_cost_usd"]
        if total_cost >= self.cost_ceiling_usd:
            print(f"\n🚨 [COST CEILING]: Chi phí ước tính ${total_cost:.4f} đã vượt ngưỡng ${self.cost_ceiling_usd:.2f}!")
            print(f"   Pipeline sẽ tiếp tục nhưng cần chú ý chi phí.")
        elif total_cost >= self.cost_ceiling_usd * 0.8:
            print(f"\n⚠️ [COST WARNING]: Chi phí ước tính ${total_cost:.4f} đã đạt 80% ngưỡng ${self.cost_ceiling_usd:.2f}.")

    def print_token_summary(self):
        """In tóm tắt chi phí token cuối pipeline."""
        usage = self.token_usage
        print(f"\n{'='*60}")
        print(f"💰 TOKEN USAGE & COST SUMMARY")
        print(f"{'='*60}")
        print(f"   📥 Total Input Tokens:  {usage['total_input_tokens']:,}")
        print(f"   📤 Total Output Tokens: {usage['total_output_tokens']:,}")
        print(f"   ♻️  Total Cached Tokens: {usage['total_cached_tokens']:,}")
        print(f"   📞 Total API Calls:     {usage['total_api_calls']}")
        print(f"   💵 Estimated Cost:      ${usage['estimated_cost_usd']:.4f}")
        print(f"   🌡️  Cost Ceiling:        ${self.cost_ceiling_usd:.2f}")
        
        if usage["per_role"]:
            print(f"\n   📊 Per-Role Breakdown:")
            for role, data in usage["per_role"].items():
                print(f"      {role}: {data['input']:,} in / {data['output']:,} out / {data['calls']} calls")
        print(f"{'='*60}\n")

    def extract_test_filter_keyword(self, plan: str, task_description: str) -> str:
        """Trích xuất keyword để filter test từ plan hoặc task description."""
        text = (plan or "") + " " + (task_description or "")
        text_lower = text.lower()
        features = ["statistics", "cart", "product", "order", "auth", "user", "post", "category",
                     "file", "website", "chat", "inbox", "idempotency", "search", "rating"]
        for feat in features:
            if feat in text_lower:
                return feat
        return "test"

    def ask_approval(self, message: str, force_ask: bool = False) -> bool:
        """Hỏi ý kiến người dùng trước khi thực thi hành động nhạy cảm hoặc phê duyệt chốt chặn."""
        if self.auto_approve:
            print(f"\n🛡️  [Harness HITL - AUTO APPROVED]: {message}")
            return True
        try:
            choice = input(f"\n🛡️  [Harness HITL]: {message}\n👉 Đồng ý thực thi? (y/n) [Mặc định: y]: ").strip().lower()
            return choice in ["", "y", "yes"]
        except Exception:
            return False

    def call_llm(self, prompt: str, system_instruction: str, stop_sequences: list[str] = None) -> str:
        """[LEGACY] Gọi LLM để lấy phân tích (text-only, không dùng function calling). Dùng cho Evaluator."""
        self.llm_router.current_role = self.current_role
        return self.llm_router.call_llm(
            prompt=prompt,
            system_instruction=system_instruction,
            build_cache_func=self._build_cache_helper,
            stop_sequences=stop_sequences
        )

    def call_llm_with_tools(self, messages: list[dict], system_instruction: str) -> dict:
        """Gọi LLM với Native Function Calling. Trả về dict chuẩn hóa."""
        self.llm_router.current_role = self.current_role
        return self.llm_router.call_llm_with_tools(
            messages=messages,
            system_instruction=system_instruction,
            tools_schema=TOOLS_SCHEMA,
            build_cache_func=self._build_cache_helper
        )

    def execute_mock_action(self, action_name: str, action_args: dict) -> str:
        """Thực thi action giả lập để trả về Observation trong Mock Mode."""
        if action_name == "execute_command":
            cmd = action_args.get("command", "")
            return format_observation(
                status="SUCCESS",
                summary=f"(MOCK) Chạy lệnh '{cmd}' thành công.",
                details="Mock output: Lệnh chạy thành công."
            )
        elif action_name == "view_source":
            filepath = action_args.get("file_path", "")
            return format_observation(
                status="SUCCESS",
                summary=f"(MOCK) Đọc file '{filepath}' thành công.",
                details="Nội dung file giả lập.",
                artifacts=[filepath]
            )
        elif action_name == "write_source":
            filepath = action_args.get("file_path", "")
            content = action_args.get("content", "")
            res = write_source_file(filepath, content)
            status = "SUCCESS" if "thành công" in res else "ERROR"
            return format_observation(
                status=status,
                summary=f"(MOCK) Ghi file '{filepath}' thành công." if status == "SUCCESS" else f"(MOCK) Lỗi ghi file '{filepath}': {res}",
                details=res,
                artifacts=[filepath]
            )
        return format_observation(
            status="ERROR",
            summary="Action không hợp lệ.",
            details="Mock mode không nhận biết action này."
        )

    def run_gan_evaluation(self, task: str) -> tuple[float, str]:
        """Thực thi vòng lặp Đánh giá Đối nghịch (GAN-Style Evaluator) đối với mã nguồn thay đổi."""
        print("\n🔍 [Harness Evaluator]: Đang chạy vòng lặp Đánh giá Đối nghịch (Adversarial Evaluation)...")
        
        # 1. Chạy test tự động để tính điểm Functionality
        print("🧪 [Harness Evaluator]: Chạy kiểm thử tự động phục vụ thang điểm Functionality...")
        filter_keyword = getattr(self, 'test_filter_keyword', 'test')
        test_code, test_out = run_dotnet_command(f"dotnet test --filter FullyQualifiedName~{filter_keyword}")
        
        total_tests = 0
        passed_tests = 0
        failed_tests = 0
        
        compiler_errors = extract_compiler_errors(test_out)
        test_failures = extract_test_errors(test_out)
        
        error_summary = ""
        if compiler_errors:
            error_summary += "\n🚨 CÁC LỖI BIÊN DỊCH PHÁT HIỆN ĐƯỢC:\n" + "\n".join(compiler_errors) + "\n"
        if test_failures:
            error_summary += "\n⚠️ CÁC BÀI KIỂM THỬ BỊ THẤT BẠI:\n" + "\n".join(test_failures) + "\n"
            
        lint_violations = check_csharp_linting()
        if lint_violations:
            error_summary += lint_violations
            
        if "Passed!" in test_out or "Passed" in test_out:
            match = re.search(r"Failed:\s*(\d+),\s*Passed:\s*(\d+)", test_out)
            if match:
                failed_tests = int(match.group(1))
                passed_tests = int(match.group(2))
                total_tests = passed_tests + failed_tests
            else:
                passed_tests = 36
                total_tests = 36
        elif test_code != 0 or compiler_errors:
            failed_tests = 1
            total_tests = 36
            
        func_score = 0.0
        if test_code == 0 and not compiler_errors:
            if total_tests > 0:
                func_score = (passed_tests / total_tests) * 10.0
            else:
                func_score = 10.0
        else:
            func_score = 0.0
            
        # 2. Lấy Git Diff (Loại bỏ shell=True)
        git_diff = ""
        try:
            result = subprocess.run(
                ["git", "diff", "HEAD"],
                shell=False,
                capture_output=True,
                encoding='utf-8',
                errors='replace'
            )
            git_diff = result.stdout or ""
        except Exception:
            git_diff = ""
            
        if not git_diff or not git_diff.strip():
            try:
                result = subprocess.run(
                    ["git", "diff", "HEAD~1"],
                    shell=False,
                    capture_output=True,
                    encoding='utf-8',
                    errors='replace'
                )
                git_diff = result.stdout or ""
            except Exception:
                git_diff = ""
                
        # 3. Tạo Prompt cho Evaluator
        evaluator_system = (
            "Bạn là một Adversarial Evaluator (Nhà đánh giá đối nghịch) cực kỳ nghiêm khắc và có chuyên môn cao về .NET 9.\n"
            "Nhiệm vụ của bạn là đánh giá những thay đổi code của Generator Agent dựa trên git diff và kết quả test.\n"
            "Hãy phê bình thẳng thắn, phát hiện mọi lỗi dù là nhỏ nhất. Đừng khen ngợi những đoạn code sơ sài.\n\n"
            "⚠️ BẮT BUỘC TUÂN THỦ NGUYÊN TẮC AN TOÀN BIÊN DỊCH & CHỐNG SỬA BỀ NỔI (ANTI-HOTFIX):\n"
            "- NẾU trong kết quả kiểm thử tự động có bất kỳ lỗi biên dịch (Compiler Errors) hoặc lỗi kiểm thử (Test Failures) nào, "
            "hoặc nếu Điểm Functionality tự động là 0.0, bạn BẮT BUỘC phải đánh giá điểm số 'functionality' là 0.0 và đặt 'weighted_score' DƯỚI 5.0.\n"
            "- Tuyệt đối KHÔNG ĐƯỢC CHẤP THUẬN (weighted_score phải < 7.5) đối với bất kỳ mã nguồn nào bị lỗi biên dịch hoặc lỗi test, bất kể thiết kế có đẹp thế nào.\n"
            "- BẮT BUỘC rà soát kỹ Git Diff để phát hiện các giải pháp đối phó, sửa bề nổi (hotfixes) như: hardcode giá trị từ file test (vd: `if (id == 99)`), bypass validation, bắt try-catch rỗng để ẩn exception. Nếu phát hiện hành vi này, bạn BẮT BUỘC phải đánh giá điểm 'craft' và 'weighted_score' DƯỚI 5.0.\n\n"
            "--- RUBRIC ĐÁNH GIÁ (Thang điểm 1.0 - 10.0) ---\n"
            "1. DESIGN QUALITY (Trọng số 0.3): Sự tuân thủ Clean Architecture. (Domain không có phụ thuộc ngoài, CQRS Commands/Queries tách biệt rõ ràng, thin controllers).\n"
            "2. ORIGINALITY & BEST PRACTICES (Trọng số 0.2): Sử dụng C# 13, Primary Constructors, File-Scoped Namespaces, Async/Await chuẩn chỉ.\n"
            "3. CRAFT & POLISH (Trọng số 0.3): Viết tài liệu XML comments (///) đầy đủ cho tất cả public elements mới, xử lý nullable reference types an toàn, FluentValidation hoàn hảo.\n"
            "4. FUNCTIONALITY (Trọng số 0.2): Điểm tự động dựa trên số test pass.\n\n"
            "Bạn BẮT BUỘC phải đưa ra cấu trúc phản hồi chi tiết bằng tiếng Việt, và kết thúc bằng một khối JSON hợp lệ chứa điểm số như sau:\n"
            "```json\n"
            "{\n"
            "  \"design_quality\": 8.0,\n"
            "  \"best_practices\": 7.5,\n"
            "  \"craft\": 6.0,\n"
            "  \"functionality\": 10.0,\n"
            "  \"weighted_score\": 7.7\n"
            "}\n"
            "```"
        )
        if self.policy_content:
            evaluator_system += f"\n\n--- CHÍNH SÁCH LẬP TRÌNH BẮT BUỘC (CODING_POLICY.md) ---\n{self.policy_content}"
        
        eval_prompt = (
            f"Yêu cầu nhiệm vụ: {task}\n\n"
            f"Điểm Functionality tự động: {func_score:.1f}/10.0\n"
            f"Tóm tắt trạng thái biên dịch/kiểm thử:\n"
            f"{error_summary if error_summary else '✅ Đã vượt qua toàn bộ quá trình biên dịch và kiểm thử thành công!'}\n\n"
            f"Đầu ra Console Test chi tiết (Tối đa 8000 ký tự):\n"
            f"```text\n{test_out[:8000]}\n```\n\n"
            f"Bản Git Diff của các thay đổi mới:\n"
            f"```diff\n{git_diff[:8000]}\n```\n\n"
            "Hãy đánh giá chi tiết theo Rubric và chấm điểm nghiêm khắc."
        )
        
        response = ""
        if self.mock_mode:
            response = (
                "### BÁO CÁO PHÂN TÍCH CỦA ADVERSARIAL EVALUATOR (GIẢ LẬP)\n\n"
                "1. **Design Quality (8.5/10)**: Mã nguồn tuân thủ Clean Architecture rất tốt. Các thực thể được tạo đúng Domain lớp.\n"
                "2. **Originality & Best Practices (8.0/10)**: Sử dụng Primary Constructor và File-Scoped namespace đẹp.\n"
                "3. **Craft & Polish (8.0/10)**: XML comments đầy đủ cho các public properties.\n"
                "4. **Functionality (10.0/10)**: Bộ tests chạy thành công 100%.\n\n"
                "**Tổng kết**: Code đạt chất lượng Senior C# rất tốt, sạch sẽ và sẵn sàng tích hợp.\n\n"
                "```json\n"
                "{\n"
                "  \"design_quality\": 8.5,\n"
                "  \"best_practices\": 8.0,\n"
                "  \"craft\": 8.0,\n"
                "  \"functionality\": 10.0,\n"
                "  \"weighted_score\": 8.5\n"
                "}\n"
                "```"
            )
        else:
            try:
                self.llm_router.current_role = "Evaluator"
                response = self.llm_router.call_llm(eval_prompt, evaluator_system, self._build_cache_helper)
            except Exception as e:
                print(f"[Harness Evaluator Error]: Không gọi được LLM Evaluator: {e}")
                # Sửa P0: Thay đổi điểm mặc định khi LLM Evaluator lỗi thành 0.0 thay vì 8.0
                response = f"{e}\n```json\n{{\"design_quality\": 0.0, \"best_practices\": 0.0, \"craft\": 0.0, \"functionality\": 0.0, \"weighted_score\": 0.0}}\n```"
                
        # Parse weighted_score từ JSON block
        weighted_score = 0.0
        try:
            json_match = re.search(r"```json\s*(.*?)\s*```", response, re.DOTALL)
            if json_match:
                score_data = json.loads(json_match.group(1))
                weighted_score = float(score_data.get("weighted_score", 0.0))
            else:
                matches = re.findall(r'"weighted_score":\s*([\d.]+)', response)
                if matches:
                    weighted_score = float(matches[-1])
        except Exception as e:
            print(f"[Harness Evaluator Warning]: Không trích xuất được điểm số JSON: {e}")
            # Sửa P0: Thay đổi điểm mặc định khi parse lỗi thành 0.0 thay vì 7.0
            weighted_score = 0.0
            
        return weighted_score, response

    def selective_rollback(self) -> str:
        """Khôi phục có chọn lọc: chỉ rollback production code, giữ nguyên test cases và docs."""
        return selective_rollback(self.modified_files)

    def generate_directory_tree(self) -> str:
        """Tự động quét cấu trúc thư mục dự án và tạo sơ đồ dạng cây để Agent hiểu kiến trúc và tránh sai đường dẫn."""
        harness_dir = os.path.dirname(os.path.abspath(__file__))
        scripts_dir = os.path.dirname(harness_dir)
        root_dir = os.path.dirname(scripts_dir)
        return generate_directory_tree(root_dir)

    def run_policy_validation(self) -> tuple[int, str]:
        """Chạy script kiểm tra coding policy tĩnh."""
        harness_dir = os.path.dirname(os.path.abspath(__file__))
        scripts_dir = os.path.dirname(harness_dir)
        root_dir = os.path.dirname(scripts_dir)
        try:
            # Sửa P0: Loại bỏ shell=True
            cmd = ['powershell.exe', '-ExecutionPolicy', 'Bypass', '-Command', './scripts/final-check.ps1 validate-policy']
            result = subprocess.run(
                cmd,
                shell=False,
                cwd=root_dir,
                capture_output=True,
                encoding='utf-8',
                errors='replace',
                timeout=60
            )
            output = (result.stdout or "") + "\n" + (result.stderr or "")
            return result.returncode, output
        except Exception as e:
            return -1, f"Lỗi thực thi validate-policy: {str(e)}"

    def condense_message_history(self, messages: list[dict]) -> list[dict]:
        """
        Nén lịch sử hội thoại dạng mảng messages để tiết kiệm token.
        Giữ nguyên tin nhắn user ban đầu và 3 lượt gần nhất (6 messages).
        Thu gọn cả assistant thoughts lẫn tool results cho các lượt cũ hơn.
        """
        if len(messages) <= 8:
            return messages
        
        condensed = []
        # Luôn giữ nguyên tin nhắn user đầu tiên (chứa context + task description)
        condensed.append(messages[0])
        
        # Xác định các cặp (assistant + tool) messages ở giữa để nén
        # Giữ nguyên 6 messages cuối (~ 3 lượt tool call gần nhất)
        middle_messages = messages[1:-6]
        tail_messages = messages[-6:]
        
        for msg in middle_messages:
            if msg["role"] == "tool":
                content = msg.get("content", "")
                # Nén nội dung OBSERVATION quá dài từ các lượt cũ
                if len(content) > 200:
                    status_match = re.search(r"STATUS:\s*(\S+)", content)
                    summary_match = re.search(r"SUMMARY:\s*(.*?)(?=\n|$)", content)
                    status = status_match.group(1) if status_match else "UNKNOWN"
                    summary = summary_match.group(1) if summary_match else "Đã thực thi"
                    
                    condensed_msg = copy.deepcopy(msg)
                    condensed_msg["content"] = (
                        f"=== OBSERVATION (ĐÃ NÉN) ===\n"
                        f"STATUS: {status}\n"
                        f"SUMMARY: {summary}\n"
                        f"===================\n"
                    )
                    condensed.append(condensed_msg)
                else:
                    condensed.append(msg)
            elif msg["role"] == "assistant":
                # Fix 6: Nén assistant thoughts cũ — chỉ giữ tool_call metadata
                condensed_msg = copy.deepcopy(msg)
                if condensed_msg.get("tool_calls"):
                    # Chỉ giữ tên tool + file_path/command, xóa thought dài
                    tc = condensed_msg["tool_calls"][0]
                    tool_name = tc["function"]["name"]
                    args = tc["function"]["arguments"]
                    if isinstance(args, str):
                        try:
                            args = json.loads(args)
                        except Exception:
                            args = {}
                    brief = args.get("file_path") or args.get("command") or args.get("summary", "")[:80]
                    condensed_msg["content"] = f"[ĐÃ NÉN] {tool_name}({brief})"
                elif condensed_msg.get("content") and len(condensed_msg["content"]) > 200:
                    condensed_msg["content"] = condensed_msg["content"][:150] + "... [ĐÃ NÉN]"
                condensed.append(condensed_msg)
            else:
                condensed.append(msg)
        
        # Giữ nguyên các messages gần nhất (3 lượt đầy đủ)
        condensed.extend(tail_messages)
        return condensed

    def distill_and_persist_lessons(self, task_description: str):
        """Tự động phân tích lịch sử log chạy và báo cáo đánh giá để đúc kết bài học kinh nghiệm vào harness_lessons.md."""
        harness_dir = os.path.dirname(os.path.abspath(__file__))
        scripts_dir = os.path.dirname(harness_dir)
        root_dir = os.path.dirname(scripts_dir)
        
        def call_llm_helper(prompt, system_instruction, role):
            self.llm_router.current_role = role
            return self.llm_router.call_llm(prompt, system_instruction, self._build_cache_helper)
            
        distill_and_persist_lessons(
            task_description=task_description,
            mock_mode=self.mock_mode,
            log_file=self.log_file,
            evals_dir=self.evals_dir,
            lessons_path=self.lessons_path,
            call_llm_func=call_llm_helper
        )

    def print_tool_call(self, tool_call: dict, thought: str = ""):
        """Hiển thị tool call từ Native Function Calling một cách trực quan trên terminal."""
        func = tool_call["function"]
        name = func["name"]
        args = func["arguments"]
        
        if thought:
            print(f"\n🧠 [THOUGHT]: {thought}")
            
        print(f"🎬 [ACTION]: 🛠  {name}")
        
        if name == "write_source":
            print(f"   ├─ 📂 Đường dẫn: {args.get('file_path', '')}")
            print(f"   └─ 📝 Nội dung: [Mã nguồn được ghi - Xem chi tiết tại Preview]")
        elif name == "view_source":
            filepath = args.get('file_path', '')
            start = args.get('start_line', '')
            end = args.get('end_line', '')
            range_info = f" (dòng {start}-{end})" if start and end else ""
            print(f"   └─ 📂 Đường dẫn: {filepath}{range_info}")
        elif name == "execute_command":
            print(f"   └─ 💻 Lệnh chạy: '{args.get('command', '')}'")
        elif name == "finish_task":
            summary = args.get('summary', '')
            summary_clean = summary.replace("\\n", "\n").replace("\n", "\n      ")
            print(f"   └─ 🏁 Báo cáo kết quả:\n      {summary_clean}")
        else:
            print(f"   └─ ⚙️ Tham số: {args}")

    def run_agent_loop(self, role: str, system_instruction: str, initial_context: str, on_finish_callback) -> str:
        """Thực thi vòng lặp ReAct (Suy nghĩ -> Hành động -> Quan sát) cho một Agent cụ thể, sử dụng Native Function Calling."""
        print(f"\n=============================================================")
        print(f"🤖 KÍCH HOẠT AGENT: [{role.upper()}]")
        print(f"=============================================================")
        
        harness_dir = os.path.dirname(os.path.abspath(__file__))
        scripts_dir = os.path.dirname(harness_dir)
        root_dir = os.path.dirname(scripts_dir)
        
        self.current_role = role
        self.iteration_count = 0
        self.action_history = []
        self.write_history = {}
        self.test_failure_history = []
        self.error_signature_history = []  # Fix 4: track compiler error signatures
        self.finish_task_rejections = 0  # Fix 5: cap finish_task rejection budget
        self.write_count_without_build = 0  # Fix 7: force build after N writes
        
        # Xác định tier model để tối giản hóa phản hồi lỗi format (arXiv:2605.26731)
        is_chat_model = self.provider in ["gemini", "openai"] and not any(kw in (os.getenv("GEMINI_API_KEY") or "").lower() for kw in ["thinking", "o1", "o3"])
        
        # Fix 1: Per-role iteration budget
        effective_max = self.role_max_iterations.get(role, self.max_iterations)
        is_infinite = (effective_max == -1)
        if is_infinite:
            effective_max = 999999
        print(f"📊 [Harness]: Budget cho {role}: " + ("vô hạn (loop tới khi xong)" if is_infinite else f"tối đa {effective_max} iterations"))
        print(f"📊 [Harness]: Global iterations: {self.global_iteration_count}/{self.absolute_max_iterations}")
        
        # Khởi tạo messages array với tin nhắn user đầu tiên
        messages = [
            {"role": "user", "content": initial_context}
        ]
        
        while self.iteration_count < effective_max:
            # === LOOP ENGINEERING: Absolute Budget Ceiling (Improvement 1) ===
            if self.global_iteration_count >= self.absolute_max_iterations:
                print(f"\n🚨 [ABSOLUTE CEILING]: Đạt giới hạn tuyệt đối {self.absolute_max_iterations} iterations trên toàn pipeline.")
                print(f"   Dừng agent {role} để bảo vệ chi phí. Không có budget expansion nào có thể vượt qua giới hạn này.")
                return ""
            
            if self.iteration_count > 0:
                time.sleep(2)
            
            # Nén lịch sử nếu quá dài
            optimized_messages = self.condense_message_history(messages)
            
            # Gọi LLM với Native Function Calling
            response = self.call_llm_with_tools(optimized_messages, system_instruction)
            
            if response is None:
                print(f"\n🚨 [Harness Error]: LLM trả về None response. Dừng agent {role}.")
                return ""
            
            thought = response.get("thought", "")
            tool_calls = response.get("tool_calls", [])
            
            # Nếu LLM không trả về tool call nào, yêu cầu retry
            if not tool_calls:
                print(f"[Harness Warning]: LLM không gọi tool nào ở lượt {self.iteration_count}. Yêu cầu gọi lại...")
                # Append assistant message (chỉ có text) và user message yêu cầu gọi tool
                if thought:
                    messages.append({"role": "assistant", "content": thought})
                
                # Cải tiến arXiv:2605.26731: Micro-correction khi phát hiện format error/no tool call
                # Prompt siêu ngắn giúp chat model định hình lại format thay vì bị nhiễu do prompt quá dài
                micro_prompt = "Bạn BẮT BUỘC phải gọi tool. Ví dụ: gọi `view_source` hoặc `write_source`. Trả về đúng cấu trúc JSON tool call!"
                if is_chat_model:
                    micro_prompt = "LỖI FORMAT: Hãy chọn 1 tool (view_source/write_source/execute_command/finish_task) và gọi ngay dưới dạng Native Function Call!"
                
                messages.append({
                    "role": "user",
                    "content": micro_prompt
                })
                self.iteration_count += 1
                self.global_iteration_count += 1  # Absolute ceiling counter
                continue
            
            # Lấy tool call đầu tiên (chỉ xử lý 1 tool mỗi lượt)
            tool_call = tool_calls[0]
            action_name = tool_call["function"]["name"]
            action_args = tool_call["function"]["arguments"]
            if isinstance(action_args, str):
                try:
                    action_args = json.loads(action_args)
                except json.JSONDecodeError:
                    action_args = {}
            
            # Hiển thị tool call
            self.print_tool_call(tool_call, thought)
            
            # Ghi log
            with open(self.log_file, "a", encoding="utf-8") as f:
                f.write(f"\n[{role} Lượt {self.iteration_count}]:\n")
                f.write(f"THOUGHT: {thought}\n")
                f.write(f"TOOL CALL: {action_name}({json.dumps(action_args, ensure_ascii=False)[:500]})\n")
            
            # === PHÁT HIỆN VÒNG LẶP (Loop Detection) ===
            # Fingerprint cho loop detection: với view_source thì include filepath (đọc file khác nhau là ok)
            # Với execute_command/write_source thì dùng full args để phát hiện loop thực sự
            if action_name == "view_source":
                # view_source với cùng file + cùng range mới là loop
                fp_key = (action_name, action_args.get("file_path", ""), action_args.get("start_line"), action_args.get("end_line"))
            else:
                fp_key = (action_name, json.dumps(action_args, sort_keys=True, ensure_ascii=False)[:200])
            self.action_history.append(fp_key)
            
            loop_warning = ""
            # Phát hiện loop: chỉ áp dụng với execute_command và write_source (không phải view_source)
            if action_name != "view_source":
                if len(self.action_history) >= 5 and all(self.action_history[-i] == fp_key for i in range(1, 6)):
                    print("\n🚨 [Harness Early Exit]: Phát hiện vòng lặp hành động trùng lặp liên tiếp 5 lần. Dừng Agent để tránh lãng phí token.")
                    rollback_log = self.selective_rollback()
                    print(f"⚠️ Hậu quả Rollback:\n{rollback_log}")
                    return "FAIL: Đã kích hoạt ngắt khẩn cấp do vòng lặp hành động trùng lặp."
                elif len(self.action_history) >= 3 and all(self.action_history[-i] == fp_key for i in range(1, 4)):
                    loop_warning = (
                        "\n🚨 [CẢNH BÁO VÒNG LẶP HỆ THỐNG]: Bạn đang thực hiện chính xác cùng một thao tác liên tục và nhận cùng kết quả/lỗi.\n"
                        "Hãy đổi chiến thuật ngay! Bạn KHÔNG được lặp lại hành động này nữa. Hãy làm một trong các việc sau:\n"
                        "  1. Sử dụng 'view_source' để đọc lại file bị lỗi để xem cấu trúc và nội dung thực tế trước khi sửa.\n"
                        "  2. Đọc các file liên quan khác để tìm giải pháp hoặc namespace đúng.\n"
                        "  3. Thực hiện thay đổi khác (ví dụ: tạo stub class trước) thay vì lặp lại thao tác lỗi."
                    )

            # Kiểm tra trùng nội dung ghi file
            if action_name == "write_source" and action_args.get("content"):
                filepath = action_args.get("file_path", "")
                content = action_args.get("content", "")
                content_hash = hash(content)
                if filepath not in self.write_history:
                    self.write_history[filepath] = []
                self.write_history[filepath].append(content_hash)
                
                if self.write_history[filepath].count(content_hash) >= 3:
                    print(f"\n🚨 [Harness Early Exit]: Phát hiện ghi trùng nội dung vào '{filepath}' 3 lần. Dừng Agent để tránh lãng phí token.")
                    rollback_log = self.selective_rollback()
                    print(f"⚠️ Hậu quả Rollback:\n{rollback_log}")
                    return "FAIL: Đã kích hoạt ngắt khẩn cấp do lặp lại nội dung ghi file."
                elif self.write_history[filepath].count(content_hash) >= 2:
                    loop_warning = (
                        f"\n🚨 [CẢNH BÁO VÒNG LẶP HỆ THỐNG]: Bạn đang ghi đè chính xác cùng một nội dung vào tệp '{filepath}' lần thứ {self.write_history[filepath].count(content_hash)}.\n"
                        "Mã nguồn này đã được áp dụng trước đó và không giúp vượt qua kiểm thử. Bạn KHÔNG được tiếp tục ghi đè nội dung này.\n"
                        "Hãy đổi chiến thuật: đọc kỹ stack trace lỗi, kiểm tra lại Mock setups, hoặc đọc các file kiểm thử tương ứng để hiểu logic."
                    )
            
            # === THỰC THI TOOL ===
            observation = ""
            
            if self.mock_mode:
                if action_name == "finish_task":
                    finish_msg = action_args.get("summary", "")
                    success, feedback = on_finish_callback(finish_msg)
                    if success:
                        print(f"✅ [Agent {role}] hoàn thành xuất sắc nhiệm vụ.")
                        return finish_msg
                    else:
                        observation = format_observation(
                            status="ERROR",
                            summary="Người đánh giá yêu cầu chỉnh sửa.",
                            details=feedback,
                            next_actions=["Điều chỉnh thiết kế hoặc sửa lỗi theo ý kiến góp ý."]
                        )
                else:
                    observation = self.execute_mock_action(action_name, action_args)
            else:
                # === Fix 2: Hard-block Planner từ execute_command ===
                if role == "Planner" and action_name == "execute_command":
                    observation = format_observation(
                        status="ERROR",
                        summary="Ràng buộc vai trò Planner: KHÔNG được chạy lệnh.",
                        details="Planner chỉ được phép dùng: view_source, write_source (stub only), finish_task. KHÔNG chạy dotnet build/test.",
                        next_actions=["Tiếp tục lập kế hoạch bằng view_source hoặc gọi finish_task."]
                    )
                elif action_name == "execute_command":
                    cmd = action_args.get("command", "")
                    allowed_cmds = self.allowed_cmds
                    is_git = cmd.startswith("git ") and any(sub in cmd for sub in ["status", "add", "diff"])
                    
                    destructive_git = ["commit", "push --force", "push -f", "rebase", "reset --hard", "cherry-pick", "merge"]
                    if any(kw in cmd.lower() for kw in destructive_git):
                        blocked = [kw for kw in destructive_git if kw in cmd.lower()][0]
                        observation = format_observation(
                            status="ERROR",
                            summary="Lỗi bảo mật Harness.",
                            details=f"Harness nghiêm cấm lệnh 'git {blocked}'. Mọi thao tác nguy hiểm (commit, push --force, rebase, reset --hard, cherry-pick, merge) phải do kỹ sư con người thực hiện thủ công.",
                            next_actions=["Chỉ dùng git status, git diff hoặc git add. Không dùng git commit hay destructive commands."]
                        )
                    elif not any(cmd.startswith(allowed) for allowed in allowed_cmds) and not is_git:
                        observation = format_observation(
                            status="ERROR",
                            summary="Lỗi bảo mật Harness.",
                            details=f"Harness nghiêm cấm chạy các lệnh tùy ý. Lệnh hợp lệ: {', '.join(allowed_cmds)} hoặc git status/diff/add.",
                            next_actions=[f"Chỉ dùng {', '.join(allowed_cmds)}, git status, git diff, git add."]
                        )
                    else:
                        is_git_change = "git " in cmd and "add" in cmd
                        if self.ask_approval(f"Agent muốn chạy lệnh hệ thống: '{cmd}'", force_ask=is_git_change):
                            code, out = run_dotnet_command(cmd)
                            status = "SUCCESS" if code == 0 else "ERROR"
                            details = out[:3000] if "build" in cmd else out[:1000]
                            test_fail_count = 0
                            # Reset write-without-build counter khi build được gọi
                            if "build" in cmd:
                                if self.write_count_without_build > 0:
                                    print(f"🔄 [Harness]: Reset write-without-build counter ({self.write_count_without_build} → 0) sau build.")
                                self.write_count_without_build = 0
                            if code != 0:
                                if "build" in cmd:
                                    comp_errs = extract_compiler_errors(out)
                                    if comp_errs:
                                        details += "\n\n--- CS COMPILER ERRORS ---\n" + "\n".join(comp_errs)
                                        
                                        # Track exact compiler error history (existing)
                                        if not hasattr(self, 'compiler_error_history'):
                                            self.compiler_error_history = []
                                        self.compiler_error_history.append(comp_errs)
                                        if len(self.compiler_error_history) >= 3 and self.compiler_error_history[-1] == self.compiler_error_history[-2] == self.compiler_error_history[-3]:
                                            print(f"\n🚨 [Harness Loop Warning]: Lỗi compiler trùng lặp liên tiếp 3 lần. Mở rộng budget.")
                                            details += "\n\n🚨 [CẢNH BÁO VÒNG LẶP HỆ THỐNG]: Lỗi compiler lặp lại 3 lần. Budget đã mở rộng. Hãy đổi chiến thuật! ĐỌC kỹ interface/class cần implement trước khi viết code.\n"
                                            effective_max += 10
                                            print(f"🔄 [Harness]: Mở rộng budget thêm 10 → {effective_max} iterations.")
                                        
                                        # Fix 4: Error-signature loop detection — đếm tổng số lần, không chỉ liên tiếp
                                        error_codes = tuple(sorted(set(re.findall(r'CS\d+', "\n".join(comp_errs)))))
                                        self.error_signature_history.append(error_codes)
                                        sig_counts = Counter(self.error_signature_history)
                                        if sig_counts[error_codes] >= 4:
                                            print(f"\n🚨 [Harness Loop Warning]: Cùng set lỗi compiler {error_codes} xuất hiện {sig_counts[error_codes]} lần (tổng cộng). Mở rộng budget.")
                                            details += f"\n\n🚨 [CẢNH BÁO VÒNG LẶP HỆ THỐNG]: Cùng set lỗi {error_codes} xuất hiện {sig_counts[error_codes]} lần. Budget đã mở rộng. Hãy đổi chiến thuật! Xem kỹ file test để biết signature đúng.\n"
                                            effective_max += 10
                                            print(f"🔄 [Harness]: Mở rộng budget thêm 10 → {effective_max} iterations.")
                                elif "test" in cmd:
                                    test_errs = extract_test_errors(out)
                                    if test_errs:
                                        test_fail_count = len(test_errs)
                                        details = "\n".join(test_errs)
                                        
                                        tags_only = []
                                        for err in test_errs:
                                            tags = [l for l in err.splitlines() if l.strip().startswith("🏷️")]
                                            tags_only.extend(tags)
                                        self.test_failure_history.append(tags_only)
                                        test_sig_counts = Counter(self.test_failure_history)
                                        if test_sig_counts[tags_only] >= 4:
                                            print(f"\n🚨 [Harness Loop Warning]: Cùng set lỗi test xuất hiện {test_sig_counts[tags_only]} lần (tổng cộng). Mở rộng budget.")
                                            details += f"\n\n🚨 [CẢNH BÁO VÒNG LẶP HỆ THỐNG]: Cùng set lỗi test lặp lại {test_sig_counts[tags_only]} lần. Budget đã mở rộng. Hãy đổi chiến thuật!\n"
                                            details += "Hướng dẫn: dùng `view_source` đọc file test và xem kỹ expected behavior, kiểm tra lại Mock setups.\n"
                                            effective_max += 10
                                            print(f"🔄 [Harness]: Mở rộng budget thêm 10 → {effective_max} iterations.")
                                        elif test_sig_counts[tags_only] >= 3:
                                                loop_warning_test = (
                                                    f"\n🚨 [CẢNH BÁO VÒNG LẶP HỆ THỐNG]: Cùng set lỗi test xuất hiện {test_sig_counts[tags_only]} lần. Hãy đổi chiến thuật.\n"
                                                    "Hãy đọc kỹ stack trace lỗi, kiểm tra lại Mock setups, hoặc đọc các file kiểm thử tương ứng để hiểu logic."
                                                )
                                                details = loop_warning_test + "\n\n" + details

                            if ("build" in cmd or "test" in cmd) and code == 0:
                                val_code, val_out = self.run_policy_validation()
                                if val_code != 0:
                                    status = "ERROR"
                                    details += "\n\n🚨 CÁC LỖI VI PHẠM CODING POLICY (Tĩnh):\n" + val_out

                            observation = format_observation(
                                status=status,
                                summary=f"Chạy lệnh '{cmd}' " + ("thành công." if status == "SUCCESS" else f"thất bại ({test_fail_count} lỗi test)." if test_fail_count > 0 else "thất bại."),
                                details=details,
                                next_actions=["Đọc DETAILS bên trên. Nếu build thất bại: sửa code bằng write_source rồi build lại. HINT: lỗi namespace/convention? Dùng view_source('CODING_POLICY.md') để xem rules. Nếu thành công: gọi finish_task để kết thúc pha."]
                            )
                        else:
                            observation = format_observation(
                                status="ERROR",
                                summary="Từ chối chạy lệnh.",
                                details="Người dùng từ chối cấp quyền chạy lệnh này.",
                                next_actions=["Dùng view_source hoặc write_source thay vì execute_command, hoặc finish_task để kết thúc."]
                            )
                            
                elif action_name == "view_source":
                    filepath = action_args.get("file_path", "")
                    start_line = action_args.get("start_line")
                    end_line = action_args.get("end_line")
                    
                    normalized_name = os.path.basename(filepath).lower()
                    if normalized_name == ".env" or "appsettings" in normalized_name or ".env." in normalized_name:
                        observation = format_observation(
                            status="ERROR",
                            summary="Lỗi bảo mật Harness.",
                            details="Nghiêm cấm đọc tệp tin cấu hình hoặc bí mật (.env, appsettings).",
                            next_actions=["Đọc file source code (.cs) trong dự án thay vì file cấu hình."]
                        )
                    elif not is_path_safe(filepath, root_dir):
                        observation = format_observation(
                            status="ERROR",
                            summary="Lỗi bảo mật Harness.",
                            details="Nghiêm cấm đọc tệp tin nằm ngoài thư mục gốc dự án.",
                            next_actions=["Chỉ đọc file trong thư mục dự án: Domain/, Application/, Infrastructure/, Controllers/, FloraCore.Tests/."]
                        )
                    else:
                        content = read_source_file(filepath, start_line, end_line)
                        status = "SUCCESS" if not content.startswith("Lỗi đọc file") else "ERROR"
                        
                        # Phân trang thông minh: nếu file quá dài và không có range, gợi ý dùng phân trang
                        truncated = False
                        if start_line is None and end_line is None and len(content) > 12000:
                            total_lines = content.count('\n') + 1
                            content = content[:12000]
                            truncated = True
                            
                        details = content
                        next_acts = ["Dùng thông tin từ file này để viết/sửa code, hoặc đọc thêm file khác."]
                        if truncated:
                            next_acts = [
                                f"File này có {total_lines} dòng và đã bị cắt ngắn. Dùng view_source(file_path, start_line, end_line) để đọc phần tiếp theo.",
                                "Dùng thông tin đã đọc được để tiếp tục công việc."
                            ]
                        
                        observation = format_observation(
                            status=status,
                            summary=f"Đọc file '{filepath}'" + (f" (dòng {start_line}-{end_line})" if start_line else "") + (" thành công." if status == "SUCCESS" else " thất bại."),
                            details=details,
                            artifacts=[filepath],
                            next_actions=next_acts
                        )
                    
                elif action_name == "write_source":
                    filepath = action_args.get("file_path", "")
                    content = action_args.get("content", "")
                    
                    if not filepath or not content:
                        observation = format_observation(
                            status="ERROR",
                            summary="write_source missing parameters",
                            details="Hãy dùng write_source với đầy đủ hai parameter: file_path và content. Cả hai không được bỏ trống.",
                            next_actions=["Gọi lại write_source với đúng cú pháp (đầy đủ file_path và content)."]
                        )
                    elif not content.strip():
                        observation = format_observation(
                            status="ERROR",
                            summary="write_source empty content string",
                            details="Parameter content không được để trống (chỉ chứa khoảng trắng). Nếu bạn muốn tạo file trống, hãy viết một nội dung hợp lệ.",
                            next_actions=["Gọi lại write_source với content không rỗng."]
                        )
                    elif not is_path_safe(filepath, root_dir):
                        observation = format_observation(
                            status="ERROR",
                            summary="Lỗi bảo mật Harness.",
                            details="Nghiêm cấm ghi tệp tin nằm ngoài thư mục gốc dự án.",
                            next_actions=["Chỉ ghi file trong thư mục dự án: Domain/, Application/, Infrastructure/, Controllers/, FloraCore.Tests/."]
                        )
                    else:
                        try:
                            rel_path = os.path.relpath(os.path.abspath(filepath), root_dir)
                        except Exception:
                            rel_path = filepath
                        normalized_path = rel_path.lower().replace("\\", "/")
                        if "ai_developer_harness.py" in normalized_path or "harness/" in normalized_path or ".env" in normalized_path or "appsettings" in normalized_path:
                            observation = format_observation(
                                status="ERROR",
                                summary="Loi bao mat Harness.",
                                details="Nghiem cam sua doi file cau hinh he thong, file harness hoac appsettings.",
                                next_actions=["Chi ghi file source code (.cs) trong du an."],
                                artifacts=[filepath]
                            )
                        elif normalized_path.endswith(".csproj") or normalized_path.endswith(".sln") or normalized_path.endswith("program.cs") or normalized_path.endswith("startup.cs"):
                            observation = format_observation(
                                status="ERROR",
                                summary="Loi bao mat Harness.",
                                details=f"Nghiem cam ghi de file cau hinh du an: '{filepath}'. Cac file .csproj, .sln, Program.cs la file cau hinh, khong duoc tu dong sua doi. Loi CS0246 co nghia la class/interface chua duoc tao - hay tao file .cs con thieu bang write_source.",
                                next_actions=["Tap trung vao loi CS0246: tao file .cs con thieu bang write_source vao dung thu muc."],
                                artifacts=[filepath]
                            )
                        elif role == "Planner" and not (
                            normalized_path.startswith("docs/")
                            or "execution_plan.md" in normalized_path
                        ):
                            observation = format_observation(
                                status="ERROR",
                                summary="Ràng buộc vai trò Planner.",
                                details="Với vai trò Planner, bạn CHỈ được phép ghi kế hoạch vào docs/. Tuyệt đối KHÔNG được ghi file .cs production, không gọi execute_command.",
                                next_actions=["Ghi kế hoạch vào docs/plans/execution_plan.md, sau đó gọi finish_task."],
                                artifacts=[filepath]
                            )
                        elif role == "TestWriter" and not ("test" in normalized_path or "tests" in normalized_path):
                            observation = format_observation(
                                status="ERROR",
                                summary="Ràng buộc vai trò TestWriter.",
                                details="Với vai trò TestWriter, bạn chỉ được phép ghi hoặc sửa đổi file test (nằm trong thư mục tests hoặc tên file chứa 'test'). Không sửa đổi code production.",
                                next_actions=["Chỉ ghi file trong thư mục FloraCore.Tests/."],
                                artifacts=[filepath]
                            )
                        else:
                            print(f"\n==========================================")
                            print(f"📄 PREVIEW FILE CẦN GHI: '{filepath}' ({role})")
                            print(f"==========================================")
                            print(content[:500] + ("\n...[còn tiếp]..." if len(content) > 500 else ""))
                            print(f"==========================================\n")
                            
                            if self.ask_approval(f"Đồng ý cho Agent ghi file: '{filepath}'?"):
                                res = write_source_file(filepath, content)
                                status = "SUCCESS" if res == "Ghi file thành công." else "ERROR"
                                if status == "SUCCESS":
                                    self.modified_files.add(os.path.abspath(filepath))
                                    if role == "Developer":
                                        self.write_count_without_build += 1
                                        if filepath not in self.pipeline_context["production_files_written"]:
                                            self.pipeline_context["production_files_written"].append(filepath)
                                    elif role == "Planner":
                                        if filepath.endswith(".cs") and filepath not in self.pipeline_context["stub_files_created"]:
                                            self.pipeline_context["stub_files_created"].append(filepath)
                                    elif role == "TestWriter":
                                        if filepath not in self.pipeline_context["test_files_created"]:
                                            self.pipeline_context["test_files_created"].append(filepath)
                                    # Auto-reload AGENTS.md nếu agent vừa ghi vào nó
                                    abs_written = os.path.abspath(filepath)
                                    if abs_written == os.path.abspath(self.agents_md_path):
                                        try:
                                            with open(self.agents_md_path, "r", encoding="utf-8") as af:
                                                self.agents_rules = af.read().strip()
                                            print(f"🔄 [Harness]: AGENTS.md đã được cập nhật. Rules reloaded ({len(self.agents_rules)} chars).")
                                        except Exception as e:
                                            print(f"⚠️ [Harness]: Lỗi reload AGENTS.md: {e}")
                                details = res
                                # Post-write build hook: auto-build .cs files cho Developer
                                if status == "SUCCESS" and role == "Developer" and filepath.endswith(".cs"):
                                    print(f"🔨 [Harness Post-Write Hook]: Auto-building sau khi ghi '{os.path.basename(filepath)}'...")
                                    b_code, b_out = run_dotnet_command("dotnet build FloraCore.csproj")
                                    self.write_count_without_build = 0
                                    lint_violations = check_csharp_linting()
                                    if b_code != 0:
                                        comp_errs = extract_compiler_errors(b_out)
                                        if comp_errs:
                                            err_text = "\n".join(comp_errs)
                                            status = "ERROR"
                                            res = f"BUILD FAILED ({len(comp_errs)} lỗi)"
                                            details = f"✅ File đã ghi thành công.\n\n🚨 BUILD FAILED ngay sau khi ghi file. Các lỗi compiler:\n{err_text}\n\nSửa lỗi bằng write_source, sau đó build lại."
                                        if lint_violations:
                                            details += f"\n\n🧹 LINT VIOLATIONS:\n{lint_violations}"
                                    else:
                                        details = f"✅ File đã ghi thành công. Auto-build PASS."
                                        if lint_violations:
                                            details += f"\n\n🧹 LINT VIOLATIONS:\n{lint_violations}"
                                next_acts = ["Tiếp tục: viết thêm file production code khác. Không cần build riêng - auto-build đã chạy."]
                                observation = format_observation(
                                    status=status,
                                    summary=res,
                                    details=details,
                                    artifacts=[filepath],
                                    next_actions=next_acts
                                )
                            else:
                                observation = format_observation(
                                    status="ERROR",
                                    summary="Từ chối ghi file.",
                                    details="Người dùng từ chối ghi tệp tin lên đĩa cứng.",
                                    next_actions=["Kiểm tra lại filepath hoặc nội dung, dùng write_source với thông tin khác."],
                                    artifacts=[filepath]
                                )
                            
                elif action_name == "finish_task":
                    finish_msg = action_args.get("summary", "")
                    success, feedback = on_finish_callback(finish_msg)
                    if success:
                        return finish_msg
                    else:
                        observation = format_observation(
                            status="ERROR",
                            summary="Yêu cầu sửa đổi từ Kỹ sư/Evaluator.",
                            details=feedback,
                            next_actions=["Dựa trên feedback chi tiết trong DETAILS, hãy chỉnh sửa lại."],
                            artifacts=["feedback_from_evaluator"]
                        )
                elif action_name == "search_codebase":
                    query = action_args.get("query", "")
                    file_glob = action_args.get("file_glob", "*.cs")
                    
                    if not query:
                        observation = format_observation(
                            status="ERROR",
                            summary="search_codebase missing query parameter",
                            details="Hãy cung cấp query (pattern cần tìm).",
                            next_actions=["Gọi lại search_codebase với query hợp lệ."]
                        )
                    else:
                        results = search_codebase(query, file_glob)
                        match_count = len([l for l in results.splitlines() if l.strip() and not l.startswith("...")])
                        observation = format_observation(
                            status="SUCCESS",
                            summary=f"Tìm kiếm '{query}' trong {file_glob}: {match_count} kết quả.",
                            details=results,
                            next_actions=["Dùng view_source để đọc chi tiết file quan tâm, hoặc tiếp tục search với query khác."]
                        )
                
                elif action_name == "patch_source":
                    filepath = action_args.get("file_path", "")
                    search_text = action_args.get("search_text", "")
                    replace_text = action_args.get("replace_text", "")
                    
                    if not filepath or not search_text:
                        observation = format_observation(
                            status="ERROR",
                            summary="patch_source missing parameters",
                            details="Cần cung cấp đầy đủ: file_path, search_text, replace_text.",
                            next_actions=["Gọi lại patch_source với đầy đủ tham số."]
                        )
                    elif not is_path_safe(filepath, root_dir):
                        observation = format_observation(
                            status="ERROR",
                            summary="Lỗi bảo mật Harness.",
                            details="Nghiêm cấm sửa tệp tin nằm ngoài thư mục gốc dự án.",
                            next_actions=["Chỉ sửa file trong thư mục dự án."]
                        )
                    elif role == "Planner":
                        observation = format_observation(
                            status="ERROR",
                            summary="Ràng buộc vai trò Planner.",
                            details="Planner không được phép sửa code. Chỉ dùng view_source và finish_task.",
                            next_actions=["Dùng view_source hoặc finish_task."]
                        )
                    elif role == "TestWriter" and not ("test" in filepath.lower() or "tests" in filepath.lower()):
                        observation = format_observation(
                            status="ERROR",
                            summary="Ràng buộc vai trò TestWriter.",
                            details="TestWriter chỉ được sửa file test.",
                            next_actions=["Chỉ patch file trong FloraCore.Tests/."]
                        )
                    else:
                        if self.ask_approval(f"Agent muốn patch file: '{filepath}'?"):
                            res = patch_source(filepath, search_text, replace_text)
                            status = "SUCCESS" if "thành công" in res.lower() else "ERROR"
                            if status == "SUCCESS":
                                self.modified_files.add(os.path.abspath(filepath))
                                # Track for structured handoff
                                if role == "Developer" and filepath not in self.pipeline_context["production_files_written"]:
                                    self.pipeline_context["production_files_written"].append(filepath)
                                elif role == "Planner":
                                    if filepath.endswith(".cs") and filepath not in self.pipeline_context["stub_files_created"]:
                                        self.pipeline_context["stub_files_created"].append(filepath)
                                elif role == "TestWriter":
                                    if filepath not in self.pipeline_context["test_files_created"]:
                                        self.pipeline_context["test_files_created"].append(filepath)
                                
                                # Auto-build cho Developer sau khi patch file .cs
                                if role == "Developer" and filepath.endswith(".cs"):
                                    print(f"🔨 [Harness Post-Patch Hook]: Auto-building sau khi patch '{os.path.basename(filepath)}'...")
                                    b_code, b_out = run_dotnet_command("dotnet build FloraCore.csproj")
                                    if b_code != 0:
                                        comp_errs = extract_compiler_errors(b_out)
                                        if comp_errs:
                                            status = "ERROR"
                                            res = f"Patch OK nhưng BUILD FAILED ({len(comp_errs)} lỗi):\n" + "\n".join(comp_errs)
                                    else:
                                        res = "Patch thành công. Auto-build PASS."
                            
                            observation = format_observation(
                                status=status,
                                summary=res,
                                details=res,
                                artifacts=[filepath],
                                next_actions=["Tiếp tục sửa code hoặc chạy test."]
                            )
                        else:
                            observation = format_observation(
                                status="ERROR",
                                summary="Từ chối patch file.",
                                details="Người dùng từ chối.",
                                next_actions=["Thử lại hoặc dùng write_source."]
                            )
                
                else:
                    observation = format_observation(
                        status="ERROR",
                        summary="Công cụ không hợp lệ.",
                        details=f"Không hỗ trợ tool '{action_name}'.",
                        next_actions=["Chỉ dùng: view_source, write_source, patch_source, search_codebase, execute_command, finish_task."]
                    )
            
            # Chèn loop_warning vào observation nếu có
            if loop_warning:
                observation = observation.replace("=== OBSERVATION ===", f"=== OBSERVATION ===\n{loop_warning}")
             
            # Ghi log observation
            with open(self.log_file, "a", encoding="utf-8") as f:
                f.write(f"\nOBSERVATION:\n{observation}\n")
                
            # === CẬP NHẬT MESSAGES ARRAY ===
            # 1. Append assistant message (thought + tool_call)
            assistant_msg = {"role": "assistant"}
            if thought:
                assistant_msg["content"] = thought
            assistant_msg["tool_calls"] = [{
                "id": tool_call["id"],
                "function": {
                    "name": action_name,
                    "arguments": action_args
                }
            }]
            messages.append(assistant_msg)
            
            # 2. Append tool result message
            # Nén observation nếu là lệnh thành công dài
            condensed_observation = observation
            if action_name == "execute_command" and "SUCCESS" in observation and len(observation) > 1500:
                cmd = action_args.get("command", "")
                condensed_observation = format_observation(
                    status="SUCCESS",
                    summary=f"Thực thi lệnh '{cmd}' thành công.",
                    details="(Chi tiết log thành công đã lược bỏ.)",
                    next_actions=["Đọc kết quả bên trên. Nếu cần chạy dotnet test hoặc finish_task."]
                )
                    
            messages.append({
                "role": "tool",
                "tool_call_id": tool_call["id"],
                "name": action_name,
                "content": condensed_observation
            })
            
            # Fix 5: Nâng thêm budget nếu finish_task bị reject — nhưng tối đa 3 lần mở rộng
            if action_name == "finish_task":
                # finish_task đã return nếu success, nếu tới đây nghĩa là bị reject
                self.finish_task_rejections += 1
                if self.finish_task_rejections <= 3:
                    extra = min(3, effective_max - self.iteration_count)
                    if extra > 0:
                        effective_max += extra
                        print(f"🔄 [Harness]: finish_task bị reject lần {self.finish_task_rejections}/3. Mở rộng budget thêm {extra} → {effective_max} iterations.")
                else:
                    print(f"🚨 [Harness]: finish_task đã bị reject {self.finish_task_rejections} lần. KHÔNG mở rộng budget thêm.")
                
            self.iteration_count += 1
            self.global_iteration_count += 1  # Absolute ceiling counter
                
        print(f"⚠️ [Harness Alert]: Agent {role} đạt giới hạn lặp tối đa.")
        return ""

    def classify_risk(self, task_description: str) -> str:
        """Phân loại rủi ro tác vụ theo FEATURE_INTAKE.md: tiny, normal, high-risk."""
        task_lower = task_description.lower()
        
        # High-risk indicators
        high_risk_keywords = [
            "migration", "database", "schema", "auth", "authentication", "payment",
            "security", "deploy", "docker", "k8s", "kubernetes", "upgrade",
            "breaking change", "nâng cấp", "thanh toán", "bảo mật", "cơ sở dữ liệu"
        ]
        if any(kw in task_lower for kw in high_risk_keywords):
            return "high-risk"
        
        # Tiny indicators
        tiny_keywords = [
            "typo", "comment", "rename", "format", "sửa lỗi chính tả",
            "thêm comment", "đổi tên", "refactor nhỏ", "fix typo", "căn chỉnh",
            "cleanup", "dọn dẹp", "xóa code thừa", "remove unused"
        ]
        if any(kw in task_lower for kw in tiny_keywords):
            return "tiny"
        
        return "normal"

    def update_test_matrix(self, task_description: str, test_files: list[str]):
        """Tự động cập nhật TEST_MATRIX.md với các behaviors mới đã kiểm thử."""
        harness_dir = os.path.dirname(os.path.abspath(__file__))
        scripts_dir = os.path.dirname(harness_dir)
        root_dir = os.path.dirname(scripts_dir)
        matrix_path = os.path.join(root_dir, "docs", "TEST_MATRIX.md")
        
        if not os.path.exists(matrix_path) or not test_files:
            return
        
        try:
            with open(matrix_path, "r", encoding="utf-8") as f:
                content = f.read()
            
            # Tạo entry mới cho mỗi test file
            new_entries = []
            for tf in test_files:
                # Trích xuất feature name từ đường dẫn test file
                basename = os.path.basename(tf).replace("Tests.cs", "").replace("Test.cs", "")
                # Kiểm tra xem behavior đã tồn tại trong matrix chưa
                if basename.lower() not in content.lower():
                    new_entries.append(
                        f"| {basename} | {task_description[:50]} | Unit | — | — | implemented |"
                    )
            
            if new_entries:
                # Append vào cuối bảng matrix
                append_text = "\n".join(new_entries) + "\n"
                with open(matrix_path, "a", encoding="utf-8") as f:
                    f.write(append_text)
                print(f"📊 [Harness]: Đã cập nhật TEST_MATRIX.md với {len(new_entries)} behavior(s) mới.")
        except Exception as e:
            print(f"⚠️ [Harness]: Lỗi cập nhật TEST_MATRIX.md: {e}")

    def execute_pipeline(self, task_description: str, skip_enricher: bool = False):
        """Thực thi toàn bộ quy trình: Lập Plan -> Viết Test -> Lập trình -> Đánh giá đối nghịch."""
        print(f"\n=============================================================")
        print(f"🚀 KHỞI ĐỘNG SPEC-DRIVEN PIPELINE CHO TÁC VỤ:")
        print(f"   👉 '{task_description}'")
        print(f"=============================================================")
        
        harness_dir = os.path.dirname(os.path.abspath(__file__))
        scripts_dir = os.path.dirname(harness_dir)
        root_dir = os.path.dirname(scripts_dir)
        
        # 1. Đọc chính sách lập trình cục bộ
        for p_file in self.policy_files:
            p_path = os.path.join(root_dir, p_file)
            if os.path.exists(p_path):
                try:
                    with open(p_path, "r", encoding="utf-8") as f:
                        self.policy_content = f.read()
                    print(f"📖 [Harness Control]: Đang nạp chính sách lập trình mới nhất từ '{p_file}'...")
                    break
                except Exception as e:
                    print(f"⚠️ [Harness Control]: Lỗi đọc file {p_file}: {e}")

        # 1b. Đọc AGENTS.md (flat markdown rulebook, inject mỗi lượt)
        self.agents_rules = ""
        if os.path.exists(self.agents_md_path):
            try:
                with open(self.agents_md_path, "r", encoding="utf-8") as f:
                    self.agents_rules = f.read().strip()
                print(f"📖 [Harness Control]: Đang nạp AGENTS.md rulebook...")
            except Exception as e:
                print(f"⚠️ [Harness Control]: Lỗi đọc AGENTS.md: {e}")

        # 1c. Feature Intake Gate — Phân loại rủi ro trước khi chạy pipeline
        risk_lane = self.classify_risk(task_description)
        print(f"\n🚦 [Feature Intake Gate]: Phân loại rủi ro → {risk_lane.upper()}")
        
        if risk_lane == "tiny":
            print("⚡ [Intake]: Tác vụ Tiny — bỏ qua Enricher & Planner, chạy trực tiếp Developer.")
            skip_enricher = True
            # Giảm budget cho tiny tasks để tiết kiệm token
            self.role_max_iterations["Planner"] = 3
            self.role_max_iterations["TestWriter"] = 5
            self.role_max_iterations["Developer"] = 15
        elif risk_lane == "high-risk":
            print("🔴 [Intake]: Tác vụ High-Risk — yêu cầu phê duyệt kế hoạch bắt buộc.")
            # Tăng budget cho high-risk tasks
            self.role_max_iterations["Planner"] = 12
            self.role_max_iterations["Developer"] = 50
            self.auto_approve = False  # Bắt buộc phê duyệt thủ công
        else:
            print("🟡 [Intake]: Tác vụ Normal — chạy pipeline chuẩn.")
        
        # 1d. Nạp nội dung CONTEXT_RULES.md để inject vào context nếu risk >= normal
        context_rules_content = ""
        context_rules_path = os.path.join(root_dir, "docs", "CONTEXT_RULES.md")
        if risk_lane != "tiny" and os.path.exists(context_rules_path):
            try:
                with open(context_rules_path, "r", encoding="utf-8") as f:
                    context_rules_content = f.read().strip()
                print(f"📖 [Harness Control]: Đang nạp Context Rules cho risk lane '{risk_lane}'...")
            except Exception as e:
                print(f"⚠️ [Harness Control]: Lỗi đọc CONTEXT_RULES.md: {e}")

        # 2. Xây dựng System Instructions tối ưu hóa token (arXiv:2605.26731)
        # Loại bỏ các mô tả triết lý rườm rà, tập trung 100% vào action guidelines và rules thực thi.
        tool_usage_note = (
            "\n--- HƯỚNG DẪN TOOL (MỖI LƯỢT GỌI ĐÚNG 1 TOOL) ---\n"
            "1. search_codebase(query, file_glob?) — TÌM KIẾM NHANH: Grep pattern trong codebase. Tiết kiệm token hơn view_source.\n"
            "2. codegraph_query(search) — Tìm kiếm định nghĩa lớp, phương thức bằng CodeGraph.\n"
            "3. codegraph_get_context(task) — Thu thập ngữ cảnh liên quan đến tác vụ kỹ thuật.\n"
            "4. view_source(file_path, start_line?, end_line?) — Đọc source file (phân trang dòng nếu file dài).\n"
            "5. patch_source(file_path, search_text, replace_text) — SỬA PHẪU THUẬT: Thay thế đoạn text trong file. TIẾT KIỆM TOKEN hơn write_source.\n"
            "6. write_source(file_path, content) — Ghi mới hoặc ghi đè TOÀN BỘ file (chỉ dùng khi tạo file mới hoặc thay đổi >50% nội dung).\n"
            "7. execute_command(command) — Chạy build/test/clean hoặc git status/diff/add.\n"
            "8. finish_task(summary) — Báo cáo kết thúc pha.\n"
            "🧠 NGUYÊN TẮC: TÌM TRƯỚC KHI ĐỌC, ĐỌC TRƯỚC KHI GHI.\n"
            "   - Bước 1: search_codebase/codegraph_query để định vị symbol.\n"
            "   - Bước 2: view_source để đọc chi tiết.\n"
            "   - Bước 3: patch_source (sửa nhỏ) hoặc write_source (tạo mới/ghi đè toàn bộ).\n"
            "   Một bước một hành động. Không đoán mò signature.\n"
        )

        architecture_guidelines = (
            "\n--- KIẾN TRÚC & CONVENTION ---\n"
            "Clean Architecture: Domain -> Application -> Infrastructure -> Controllers.\n"
            "Mọi import nội bộ bắt buộc dùng tiền tố: `using FloraCore.` (Cấm dùng using Application/Infrastructure bừa bãi).\n"
        )

        planner_system = (
            "Bạn là AI Software Architect trên dự án .NET 9 FloraCore (Clean Architecture).\n"
            "Nhiệm vụ: Khảo sát mã nguồn, lập Bản kế hoạch thực thi (AI Execution Plan) và tạo các file skeleton rỗng.\n"
            "🚨 RÀNG BUỘC PHÂN VAI:\n"
            "  - TUYỆT ĐỐI KHÔNG ghi logic thực tế vào file .cs. TUYỆT ĐỐI KHÔNG gọi `execute_command`.\n"
            "🚨 QUY TRÌNH BẮT BUỘC:\n"
            "  1. Không gọi finish_task ở lượt đầu tiên.\n"
            "  2. Khảo sát cấu trúc hiện có qua codegraph_query/codegraph_get_context.\n"
            "  3. Viết Bản kế hoạch Markdown chi tiết vào file `docs/plans/execution_plan.md`.\n"
            "  4. Tạo các file khung xương/stub rỗng (chỉ khai báo namespace đúng, constructor signature và ném NotImplementedException) cho toàn bộ class dự kiến tạo mới.\n"
            "  5. Gọi `finish_task` sau khi hoàn thành 4 bước trên.\n"
        )
        planner_system += architecture_guidelines
        if self.agents_rules:
            planner_system += f"\n--- AGENTS.MD RULES (BẮT BUỘC TUÂN THỦ, ƯU TIÊN CAO NHẤT) ---\n{self.agents_rules}\n"
        planner_system += tool_usage_note

        testwriter_system = (
            "Bạn là QA/Developer viết unit & integration test (xUnit, FluentAssertions) cho FloraCore.\n"
            "Nhiệm vụ: Viết bộ test case phản ánh đúng Bản kế hoạch được duyệt.\n"
            "🚨 RÀNG BUỘC VAI TRÒ:\n"
            "  - CHỈ được sửa/tạo file trong thư mục test (`FloraCore.Tests`). KHÔNG đụng vào code production logic!\n"
            "🚨 BẮT BUỘC TUÂN THỦ: Bạn bắt buộc phải tuân thủ nghiêm ngặt toàn bộ các tiêu chuẩn kiểm thử và quy tắc đặt tên được quy định trong tệp `CODING_POLICY.md` (nằm ở thư mục gốc dự án, được nạp chi tiết bên dưới).\n"
            "Gọi `finish_task` báo cáo danh sách file test đã viết khi hoàn thành."
        )
        testwriter_system += architecture_guidelines
        if self.agents_rules:
            testwriter_system += f"\n--- AGENTS.MD RULES (BẮT BUỘC TUÂN THỦ, ƯU TIÊN CAO NHẤT) ---\n{self.agents_rules}\n"
        testwriter_system += tool_usage_note

        developer_system = (
            "Bạn là Kỹ sư .NET 9 Senior trên dự án FloraCore.\n"
            "Nhiệm vụ: Hiện thực logic nghiệp vụ trong Domain, Application, Infrastructure, Controllers để pass toàn bộ tests.\n"
            "🚨 BẮT BUỘC TUÂN THỦ: Bạn bắt buộc phải tuân thủ nghiêm ngặt 100% các tiêu chuẩn lập trình sạch (Clean Code), SOLID, DI Primary Constructor, null check, và cách thiết kế pattern (CQRS, Repository) được đặc tả chi tiết trong tệp `CODING_POLICY.md` (nằm ở thư mục gốc dự án, được nạp chi tiết bên dưới). Tuyệt đối không được vi phạm.\n"
            "\n🚨 NGUYÊN TẮC CHẨN ĐOÁN LỖI (RCA) - THOUGHT PHẢI CÓ:\n"
            "  1. SYMPTOMS (Lỗi nhận được là gì?) | 2. TRACE (Dòng nào, stack trace?) | 3. ROOT CAUSE (Tại sao lỗi?) | 4. RESOLUTION (Hướng fix triệt để)\n"
            "\n🚨 QUY TRÌNH PHÁT TRIỂN:\n"
            "  - BƯỚC 0: ĐỌC test expectation TRƯỚC: dùng `codegraph_query` để tìm file test, xem bằng `view_source` trong FloraCore.Tests/ để biết method signature, expected behavior.\n"
            "  - BƯỚC 1: Viết 1 file production → BUILD NGAY (`execute_command('dotnet build FloraCore.csproj')`). KHÔNG viết nhiều file cùng lúc. Mỗi lần viết 1 file, build ngay, sửa lỗi, rồi mới viết file tiếp theo.\n"
            "  - BƯỚC 2: Chạy test bằng `execute_command('dotnet test --filter FullyQualifiedName~<KEYWORD>')`. Sửa tất cả lỗi test TRONG MỘT LẦN.\n"
            "  - BƯỚC 3: Chỉ khi Build và Test PASS 100% không vi phạm coding policy mới gọi `finish_task`.\n"
            "\n🚨 THAM KHẢO FILE MẪU CHUẨN C# 12+ THEO TỪNG TẦNG (ĐỌC TRƯỚC KHI CODE):\n"
            "  * [DOMAIN EVENTS] [OrderCreatedEvent.cs](file:///{root_dir.replace(chr(92), '/')}/Application/Features/Orders/Events/OrderCreatedEvent.cs)\n"
            "  * [APPLICATION HANDLER] [CreateWebsiteInfoCommandHandler.cs](file:///{root_dir.replace(chr(92), '/')}/Application/Features/WebsiteInfo/Commands/CreateWebsiteInfoCommandHandler.cs)\n"
            "  * [DOMAIN EVENT HANDLER] [OrderCreatedEventHandler.cs](file:///{root_dir.replace(chr(92), '/')}/Application/Features/Orders/Events/OrderCreatedEventHandler.cs)\n"
            "  * [INFRASTRUCTURE SERVICE] [AdminNotificationService.cs](file:///{root_dir.replace(chr(92), '/')}/Infrastructure/Services/AdminNotificationService.cs)\n"
            "  * [INFRASTRUCTURE REPOSITORY] [ProductRepository.cs](file:///{root_dir.replace(chr(92), '/')}/Infrastructure/Repositories/ProductRepository.cs)\n"
            "  * [WEB CONTROLLERS] [OrdersController.cs](file:///{root_dir.replace(chr(92), '/')}/Controllers/OrdersController.cs)\n"
        )
        developer_system += architecture_guidelines
        if self.agents_rules:
            developer_system += f"\n--- AGENTS.MD RULES (BẮT BUỘC TUÂN THỦ, ƯU TIÊN CAO NHẤT) ---\n{self.agents_rules}\n"
        developer_system += tool_usage_note

        # --- CALLBACK PHA 1: PLANNING ---
        def on_planner_finish(plan_content: str):
            print("\n=============================================================")
            print("📋 BẢN KẾ HOẠCH THỰC THI (PROPOSED EXECUTION PLAN):")
            print("=============================================================")
            print(plan_content)
            print("=============================================================\n")
            
            plan_dir = os.path.join(root_dir, "docs", "plans")
            os.makedirs(plan_dir, exist_ok=True)
            plan_path = os.path.join(plan_dir, "execution_plan.md")
            
            # Bảo vệ bản kế hoạch chi tiết đã ghi bằng write_source khỏi bị ghi đè bởi tóm tắt finish_task
            use_disk_plan = False
            if os.path.exists(plan_path):
                try:
                    with open(plan_path, "r", encoding="utf-8") as f:
                        disk_content = f.read()
                    if len(disk_content.strip()) > len(plan_content.strip()) and "#" in disk_content:
                        print("📖 [Harness]: Phát hiện file execution_plan.md chi tiết đã tồn tại trên đĩa. Sử dụng nội dung này.")
                        plan_content = disk_content
                        use_disk_plan = True
                except Exception as e:
                    print(f"⚠️ Lỗi đọc kiểm tra file kế hoạch trên đĩa: {e}")

            if not use_disk_plan:
                try:
                    with open(plan_path, "w", encoding="utf-8") as f:
                        f.write(plan_content)
                    print(f"💾 Đã lưu kế hoạch vào: docs/plans/execution_plan.md")
                except Exception as e:
                    print(f"⚠️ Không thể lưu file kế hoạch: {e}")
                
            production_files = []
            path_pattern = r"(?:Application/Features/\S+\.cs|Infrastructure/\S+\.cs|Controllers/\S+\.cs|Domain/\S+\.cs)"
            matches = re.findall(path_pattern, plan_content)
            for m in matches:
                cleaned = m.strip('`').strip()
                if cleaned not in production_files:
                    production_files.append(cleaned)
            self.planner_production_files = production_files
            self.test_filter_keyword = self.extract_test_filter_keyword(plan_content, task_description)
            self.pipeline_context["files_to_implement"] = self.planner_production_files
            self.pipeline_context["approved_plan"] = plan_content
            self.pipeline_context["risk_lane"] = risk_lane
            print(f"🔑 [Harness]: Test filter keyword: '{self.test_filter_keyword}'")
            if production_files:
                print(f"📋 [Harness]: Phát hiện {len(production_files)} file production code cần tạo trong kế hoạch: {', '.join(production_files)}")
                
            approved = self.ask_approval("Bạn có đồng ý phê duyệt Bản kế hoạch thực thi này không?", force_ask=True)
            if approved:
                # Đọc lại bản kế hoạch từ ổ đĩa đề phòng trường hợp lập trình viên chỉnh sửa thủ công
                try:
                    if os.path.exists(plan_path):
                        with open(plan_path, "r", encoding="utf-8") as f:
                            disk_plan_content = f.read()
                        if disk_plan_content.strip() != plan_content.strip():
                            print("\n⚙️ [Harness Control]: Phát hiện bản kế hoạch đã được chỉnh sửa thủ công trên đĩa. Đang nạp lại...")
                            plan_content = disk_plan_content
                            
                            # Phân tích lại danh sách files và test filter keyword
                            production_files = []
                            matches = re.findall(path_pattern, plan_content)
                            for m in matches:
                                cleaned = m.strip('`').strip()
                                if cleaned not in production_files:
                                    production_files.append(cleaned)
                            self.planner_production_files = production_files
                            self.test_filter_keyword = self.extract_test_filter_keyword(plan_content, task_description)
                            self.pipeline_context["files_to_implement"] = self.planner_production_files
                            self.pipeline_context["approved_plan"] = plan_content
                            print(f"🔄 [Harness Reloaded]: Cập nhật Test filter keyword: '{self.test_filter_keyword}'")
                            if production_files:
                                print(f"📋 [Harness Reloaded]: Cập nhật danh sách {len(production_files)} file production: {', '.join(production_files)}")
                except Exception as e:
                    print(f"⚠️ Lỗi khi nạp lại kế hoạch từ ổ đĩa: {e}")
                
                self.current_plan = plan_content
                return True, ""
            else:
                feedback = ""
                if sys.stdin.isatty():
                    try:
                        feedback = input("\n📝 Nhập ý kiến góp ý/yêu cầu sửa đổi của bạn cho bản kế hoạch:\n👉 ")
                    except Exception:
                        feedback = "Kế hoạch bị từ chối."
                else:
                    feedback = "Kế hoạch bị từ chối thông qua điều khiển từ xa (Telegram/Zalo)."
                return False, f"Kỹ sư con người từ chối phê duyệt bản kế hoạch này với ý kiến đóng góp sau:\n{feedback}"

        # --- CALLBACK PHA 2: TEST WRITING ---
        def on_testwriter_finish(test_report: str):
            print("\n=============================================================")
            print("🧪 THÔNG BÁO TỪ TESTWRITER AGENT:")
            print("=============================================================")
            print(test_report)
            print("=============================================================\n")
            
            test_file_pattern = r"FloraCore\.Tests/\S+\.cs|FloraCore\.Tests\\\S+\.cs"
            found_test_files = re.findall(test_file_pattern, test_report)
            self.test_writer_files = list(set(found_test_files))
            if self.test_writer_files:
                print(f"📋 [Harness]: Phát hiện {len(self.test_writer_files)} file test: {', '.join(self.test_writer_files)}")
                for tf in self.test_writer_files:
                    if tf not in self.pipeline_context["test_files_created"]:
                        self.pipeline_context["test_files_created"].append(tf)
                
                # Extract test signatures
                sigs = []
                for tf in self.test_writer_files:
                    tf_path = os.path.join(root_dir, tf) if not os.path.isabs(tf) else tf
                    if os.path.exists(tf_path):
                        try:
                            with open(tf_path, 'r', encoding='utf-8') as f:
                                tf_content = f.read()
                            # Match Fact/Theory test methods
                            matches = re.findall(
                                r"\[(Fact|Theory)\]\s*(?:[\w\s\(\)\=\,\.\"]+\s*)?(?:public\s+)?(?:async\s+Task|void)\s+(\w+)",
                                tf_content
                            )
                            sigs.extend([m[1] for m in matches if m[1]])
                        except Exception:
                            pass
                self.pipeline_context["test_signatures"] = list(set(sigs))
                self.pipeline_context["test_filter_keyword"] = self.test_filter_keyword
            
            if self.mock_mode:
                approved = self.ask_approval("Bạn có phê duyệt bộ Test Cases này không?", force_ask=True)
                if approved:
                    return True, ""
                feedback = ""
                if sys.stdin.isatty():
                    try:
                        feedback = input("\n📝 Nhập ý kiến góp ý cho bộ test cases:\n👉 ")
                    except Exception:
                        feedback = "Bộ test cases bị từ chối."
                else:
                    feedback = "Bộ test cases bị từ chối thông qua điều khiển từ xa (Telegram/Zalo)."
                return False, f"Kỹ sư từ chối phê duyệt với phản hồi:\n{feedback}"
                
            print("⚙️ Đang biên dịch bộ test mới...")
            code, out = run_dotnet_command("dotnet build")
            if code != 0:
                print("❌ Lỗi biên dịch bộ tests!")
                errs = extract_compiler_errors(out)
                
                critical_errs = []
                for err in errs:
                    missing_symbol_codes = [
                        "CS0246", "CS0234", "CS0103", "CS1061", "CS1729", 
                        "CS0117", "CS1503", "CS0122", "CS0029", "CS1501", 
                        "CS0426", "CS0120", "CS0266"
                    ]
                    is_missing_symbol = any(code_id in err for code_id in missing_symbol_codes)
                    if not is_missing_symbol:
                        critical_errs.append(err)
                        
                if critical_errs:
                    return False, f"Lỗi biên dịch bộ test mới (Lỗi cú pháp/Cấu trúc thực tế):\n" + "\n".join(critical_errs)
                else:
                    print("ℹ️ Phát hiện lỗi biên dịch do chưa có code production tương ứng (chấp nhận ở pha TestWriter). Tiếp tục...")
                
            print("\n🔍 Git Diff của thư mục test:")
            diff_res = subprocess.run(
                ["git", "diff", "HEAD", "--", "*Tests*"],
                shell=False,
                capture_output=True,
                encoding='utf-8',
                errors='replace'
            )
            print(diff_res.stdout or "Không phát hiện thay đổi trong thư mục test.")
            
            approved = self.ask_approval("Bạn có phê duyệt bộ Test Cases trên không?", force_ask=True)
            if approved:
                return True, ""
            else:
                feedback = input("\n📝 Nhập ý kiến góp ý/yêu cầu sửa đổi cho bộ test cases:\n👉 ")
                return False, f"Kỹ sư con người không phê duyệt bộ test cases này với lý do:\n{feedback}"

        # --- CALLBACK PHA 3 & 4: IMPLEMENTATION & EVALUATION ---
        def on_developer_finish(dev_report: str):
            print("\n=============================================================")
            print("💻 THÔNG BÁO TỪ DEVELOPER AGENT:")
            print("=============================================================")
            print(dev_report)
            print("=============================================================\n")
            
            # Luôn biên dịch và chạy kiểm thử thực tế trên đĩa cứng để xác thực, không tin vào văn bản báo cáo
            print("⚙️ Chạy build hệ thống (production code)...")
            code, out = run_dotnet_command("dotnet build FloraCore.csproj")
            if code != 0:
                self.pipeline_context["last_build_status"] = False
                errs = extract_compiler_errors(out)
                missing_files = []
                if hasattr(self, 'planner_production_files') and self.planner_production_files:
                    for file in self.planner_production_files:
                        file_path = os.path.join(root_dir, file) if not os.path.isabs(file) else file
                        if not os.path.exists(file_path):
                            missing_files.append(file)
                
                if missing_files:
                    feedback_msg = "PRODUCTION CODE THIẾU FILE:\n"
                    feedback_msg += "\n".join([f"- {file}" for file in missing_files])
                    feedback_msg += "\n\nHãy dừng `finish_task` lại và sử dụng `write_source` để viết đầy đủ các file production code này trước khi build. VIẾT HẾT -> BUILD."
                    return False, feedback_msg
                
                return False, "PRODUCTION CODE BUILD FAILED. Hãy viết THÊM production code hoặc sửa lỗi compiler trước khi build lại. HINT: Lỗi namespace/convention? Dùng `view_source('CODING_POLICY.md')` để xem rules. Xem lỗi compiler bên dưới để định hướng file nào cần tạo/sửa:\n" + "\n".join(errs)
                
            self.pipeline_context["last_build_status"] = True
            print("🧹 Kiểm tra Coding Policy tĩnh...")
            val_code, val_out = self.run_policy_validation()
            if val_code != 0:
                return False, "Phát hiện lỗi vi phạm Coding Policy (Tĩnh). Dùng `view_source('CODING_POLICY.md')` để xem rules cụ thể, tìm section liên quan đến lỗi bên dưới, rồi sửa code bằng `write_source`. Chi tiết:\n" + val_out
                
            print("⚙️ Chạy tests hệ thống (chỉ chạy test liên quan đến feature mới)...")
            filter_keyword = getattr(self, 'test_filter_keyword', 'test')
            t_code, t_out = run_dotnet_command(f"dotnet test --filter FullyQualifiedName~{filter_keyword}")
            if t_code != 0:
                errs = extract_test_errors(t_out)
                tags = set()
                for e in errs:
                    for line in e.splitlines():
                        if line.strip().startswith("🏷️"):
                            tags.add(line.strip())
                tag_summary = "\n".join(sorted(tags)) if tags else ""
                self.pipeline_context["last_test_summary"] = f"FAIL:\n{(tag_summary + '\n\n' if tag_summary else '') + '\n'.join(errs)}"
                return False, "TESTS FAIL: Sửa tất cả lỗi dưới đây TRONG MỘT LẦN (không chạy lại test sau mỗi lỗi).\n\n" + (tag_summary + "\n\n" if tag_summary else "") + "\n".join(errs)
                
            score, report = self.run_gan_evaluation(task_description)
            print(f"\n🏆 [EVALUATION SCORECARD]: THANG ĐIỂM ĐẠT ĐƯỢC: {score:.2f}/10.0 (Yêu cầu tối thiểu: {self.pass_threshold})")
            print(report)
            
            report_path = os.path.join(self.evals_dir, "evaluation_report.md")
            try:
                with open(report_path, "w", encoding="utf-8") as f:
                    f.write(report)
            except Exception as e:
                print(f"⚠️ Lỗi ghi report: {e}")
                
            if score >= self.pass_threshold:
                print("\n✅ Vượt qua vòng thẩm định chất lượng!")
                self.pipeline_context["last_test_summary"] = f"PASS (Score: {score:.2f}/10.0)"
                return True, ""
            else:
                rollback_log = self.selective_rollback()
                print(f"\n⚠️ Rollback log:\n{rollback_log}")
                return False, (
                    f"Hệ thống Evaluator từ chối phê duyệt code triển khai vì chưa đạt chuẩn chất lượng (Điểm: {score:.2f} < {self.pass_threshold}).\n"
                    f"--- BÁO CÁO PHÊ BÌNH CỦA EVALUATOR ---\n{report}\n\n"
                    f"--- HẬU QUẢ ROLLBACK ---\n{rollback_log}\n\n"
                    f"Vui lòng thiết kế hướng đi khác để hoàn thành task đạt chất lượng hơn."
                )

        # 4. CHẠY PIPELINE
        # 4. CHẠY PIPELINE
        # Cải tiến arXiv:2605.26731: Dynamic Directory Tree lọc theo context nhiệm vụ để tránh nhiễu Wrong File
        full_tree = self.generate_directory_tree()
        
        # Lọc thông minh: Nếu task đề cập đến order, website, statistics, v.v., lọc các thư mục liên quan
        task_lower = task_description.lower()
        related_keywords = ["order", "website", "notification", "user", "statistics", "report", "cart", "product", "auth"]
        found_keywords = [kw for kw in related_keywords if kw in task_lower]
        
        if found_keywords and len(full_tree.splitlines()) > 50:
            # Lọc bớt các nhánh không liên quan để giảm token và nhiễu (Wrong File error prevention)
            filtered_lines = []
            skip_block = False
            for line in full_tree.splitlines():
                # Nếu đi vào thư mục Test lớn không liên quan thì skip bớt
                if "Tests" in line and not any(kw in line.lower() for kw in found_keywords):
                    continue
                filtered_lines.append(line)
            directory_tree = "\n".join(filtered_lines[:120]) # Giới hạn tối đa 120 dòng quan trọng
            if len(filtered_lines) > 120:
                directory_tree += "\n... (Đã lược bớt các thư mục không liên quan để tránh nhiễu) ..."
        else:
            directory_tree = "\n".join(full_tree.splitlines()[:150]) # Fallback tree ngắn gọn
            
        context_tree_header = (
            f"SƠ ĐỒ CẤU TRÚC THƯ MỤC DỰ ÁN LIÊN QUAN (Tránh tạo file rác hoặc ghi sai đường dẫn):\n"
            f"```text\n{directory_tree}\n```\n\n"
        )
        
        # Cải tiến arXiv:2605.26731: Tier-Aware Harness Complexity Tuning
        # Chat models như Gemini 3.5 Flash cực kỳ nhạy cảm với hệ thống prompt rườm rà (Harness-Complexity Paradox)
        is_chat_model = self.provider in ["gemini", "openai"] and not any(kw in (os.getenv("GEMINI_API_KEY") or "").lower() for kw in ["thinking", "o1", "o3"])
        
        # --- CHẠY PHA 0: PROMPT ENRICHER (LÀM GIÀU YÊU CẦU TỰ ĐỘNG) ---
        enriched_task_description = task_description
        env_skip = (os.getenv("HARNESS_SKIP_ENRICHER") or "").lower() in ["1", "true", "yes"]
        
        if skip_enricher or env_skip:
            print("\n⏭️  [Harness]: Đã bỏ qua Pha 0 (Prompt Enricher) theo cấu hình/flag.")
        else:
            print("\n=============================================================")
            print("🧠 [Harness]: Khởi động Pha 0: Prompt Enricher Agent...")
            print("=============================================================")
            
            if is_chat_model:
                print("⚡ [Harness Mode]: Kích hoạt Tier-Aware Tối Giản Hóa Prompt cho Chat Model (Giảm thiểu Format Violations)")
                enricher_system = (
                    "Bạn là một Senior Prompt Engineer & C# Architect.\n"
                    "Nhiệm vụ của bạn là phân tích yêu cầu từ người dùng và viết một Bản Đặc Tả Yêu Cầu Kỹ Thuật (Enriched Prompt) cực ngắn, đi thẳng vào các điểm kỹ thuật:\n"
                    "  1. ROOT NAMESPACE: Nhắc nhở sử dụng đúng namespace `FloraCore`.\n"
                    "  2. FILE SIGNATURES: Chỉ rõ tên các file/interface quan trọng có sẵn liên quan.\n"
                    "  3. FILES TO READ: Danh sách các file cần dùng view_source để đọc trước.\n"
                    "Chỉ trả về bản đặc tả Markdown chi tiết, không giải thích thừa."
                )
            else:
                enricher_system = (
                    "Bạn là một Senior Prompt Engineer & C# Architect chuyên về .NET 9.\n"
                    "Nhiệm vụ của bạn là nhận một yêu cầu vắn tắt từ người dùng, đối chiếu với cấu trúc cây thư mục hiện tại của dự án FloraCore,\n"
                    "và sinh ra một Bản Đặc Tả Yêu Cầu Kỹ Thuật (Enriched Prompt/Task Description) cực kỳ chi tiết cho Planner Agent.\n\n"
                    "Bản Đặc Tả của bạn BẮT BUỘC phải làm rõ:\n"
                    "  1. ROOT NAMESPACE: Nhắc nhở Planner và các Agent khác sử dụng đúng root namespace `FloraCore`.\n"
                    "  2. STRUCT/SIGNATURE CONSTRAINTS: Tìm kiếm và chỉ rõ các file class/interface quan trọng đã tồn tại trong thư mục liên quan đến task để tránh việc các Agent tự đoán mò signature (Ví dụ: Class Address có 3 tham số, NotificationHub nằm tại Infrastructure/Hubs, v.v.).\n"
                    "  3. CÁC FILE LIÊN QUAN CẦN ĐỌC: Đưa ra danh sách các file C# hiện hữu mà các Agent nên dùng view_source để đọc trước.\n"
                    "  4. TIÊU CHÍ THÀNH CÔNG RÕ RÀNG: Đặc tả các test cases bắt buộc phải pass.\n\n"
                    "Đầu ra của bạn chỉ chứa bản đặc tả kỹ thuật Markdown chi tiết bằng tiếng Việt, không chứa bất kỳ lời giải thích thừa thãi nào khác."
                )
            
            enricher_prompt = (
                f"Yêu cầu vắn tắt từ User: {task_description}\n\n"
                f"Sơ đồ cấu trúc thư mục hiện tại:\n"
                f"```text\n{directory_tree}\n```\n"
                "Hãy phân tích và viết một Bản Đặc Tả Kỹ Thuật chi tiết để chuyển tiếp cho Planner."
            )
            
            if not self.mock_mode:
                try:
                    self.llm_router.current_role = "Enricher"
                    enriched_task_description = self.llm_router.call_llm(enricher_prompt, enricher_system, self._build_cache_helper)
                    print("\n✨ [Harness Enricher]: Đã làm giàu yêu cầu thành công!")
                    print("-------------------------------------------------------------")
                    print(enriched_task_description)
                    print("-------------------------------------------------------------\n")
                except Exception as e:
                    print(f"⚠️ [Harness Enricher Warning]: Lỗi làm giàu prompt: {e}. Sử dụng prompt gốc.")
            else:
                enriched_task_description = (
                    f"### BẢN ĐẶC TẢ KỸ THUẬT CHI TIẾT (LÀM GIÀU TỰ ĐỘNG)\n\n"
                    f"- **Nhiệm vụ chính**: {task_description}\n"
                    f"- **Root Namespace**: Bắt buộc dùng `FloraCore` cho tất cả các class/interface mới tạo.\n"
                    f"- **Ràng buộc dependencies**:\n"
                    f"  - Sử dụng `using FloraCore.Application.Common.Interfaces;` cho các interfaces.\n"
                    f"  - Đối với các file tests, mock các service theo chuẩn có sẵn.\n"
                )
                print("\n✨ [Harness Enricher (MOCK)]: Sử dụng bản đặc tả giả lập.")
                print(enriched_task_description)
            
        self.pipeline_context["enriched_task"] = enriched_task_description
        
        # --- CHẠY PHA 1: PLANNING ---
        self.current_plan = ""
        plan = self.run_agent_loop(
            role="Planner",
            system_instruction=planner_system,
            initial_context=context_tree_header + f"Yêu cầu nhiệm vụ chi tiết đã được làm giàu qua Pha 0:\n\n{enriched_task_description}\n\nHãy khảo sát mã nguồn và lập Bản kế hoạch thực thi (AI Execution Plan) chi tiết. Sử dụng finish_task để gửi kế hoạch.",
            on_finish_callback=on_planner_finish
        )
        if not plan:
            print("❌ Pha lập kế hoạch thất bại hoặc bị ngắt sớm. Dừng pipeline.")
            return

        if not self.current_plan:
            self.current_plan = plan

        # --- CHẠY PHA 2: TEST WRITING ---
        # Fix 7: Build structured context từ pipeline_context
        stub_context = ""
        if self.pipeline_context["stub_files_created"]:
            stub_list = "\n".join([f"  - {f}" for f in self.pipeline_context["stub_files_created"]])
            stub_context = f"\n\n📂 CÁC FILE STUB ĐÃ ĐƯỢC PLANNER TẠO TRƯỚC:\n{stub_list}\n(Bạn có thể dùng view_source để đọc các file này khi viết test.)\n"
        
        test_report = self.run_agent_loop(
            role="TestWriter",
            system_instruction=testwriter_system,
            initial_context=context_tree_header + f"Bản kế hoạch đã duyệt:\n{self.current_plan}\n{stub_context}\nHãy viết các file unit/integration test phản ánh đúng đặc tả này. Chỉ sửa thư mục test. Gọi finish_task khi hoàn thành.",
            on_finish_callback=on_testwriter_finish
        )
        if not test_report:
            print("❌ Pha viết test thất bại hoặc bị ngắt sớm. Dừng pipeline.")
            return

        # --- CHẠY PHA 3 & 4: LẬP TRÌNH VÀ THẨM ĐỊNH ĐỐI NGHỊCH ---
        # Fix 7: Build structured context cho Developer
        test_files_context = ""
        if self.pipeline_context["test_files_created"] or self.test_writer_files:
            all_test_files = list(set(self.pipeline_context["test_files_created"] + self.test_writer_files))
            test_files_list = "\n".join([f"  - {f}" for f in all_test_files])
            test_files_context = f"\n\n🧪 CÁC FILE TEST ĐÃ ĐƯỢC VIẾT:\n{test_files_list}\n(Dùng view_source để đọc nếu cần hiểu test expectations.)\n"
        
        dev_report = self.run_agent_loop(
            role="Developer",
            system_instruction=developer_system,
            initial_context=context_tree_header + f"Bản kế hoạch:\n{self.current_plan}\n\nBáo cáo test cases:\n{test_report}\n{stub_context}{test_files_context}\nHãy bắt đầu sửa code production để pass tất cả tests. Gọi finish_task khi hoàn tất.",
            on_finish_callback=on_developer_finish
        )
        if not dev_report:
            print("❌ Pha lập trình/thẩm định thất bại hoặc bị ngắt sớm. Dừng pipeline.")
            return
            
        print("\n🎉 [PIPELINE SUCCESS]: Đã hoàn thành tác vụ xuất sắc thông qua Spec-Driven Development!")
        
        # Post-pipeline: Cập nhật Test Matrix với các test files mới
        all_test_files = list(set(self.pipeline_context.get("test_files_created", []) + self.test_writer_files))
        if all_test_files:
            self.update_test_matrix(task_description, all_test_files)
        
        self.distill_and_persist_lessons(task_description)
        
        # === LOOP ENGINEERING: Token Cost Summary (Improvement 2) ===
        self.print_token_summary()
        
        # Save token report to evals directory
        try:
            token_report_path = os.path.join(self.evals_dir, "token_usage_report.json")
            import json
            with open(token_report_path, "w", encoding="utf-8") as f:
                json.dump({
                    "task": task_description,
                    "token_usage": self.token_usage,
                    "pricing": self.pricing,
                    "absolute_ceiling": self.absolute_max_iterations,
                    "global_iterations_used": self.global_iteration_count,
                }, f, ensure_ascii=False, indent=2)
            print(f"💾 [Harness]: Token report saved to {token_report_path}")
        except Exception as e:
            print(f"⚠️ [Harness]: Lỗi ghi token report: {e}")
