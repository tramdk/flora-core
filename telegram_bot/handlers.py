import asyncio
import queue
import subprocess
from telegram import Update, InlineKeyboardButton, InlineKeyboardMarkup
from telegram.ext import ContextTypes

from telegram_bot.harness_bridge import (
    PROJECT_ROOT,
    _harness_store,
    _start_pipeline_thread,
    stream_output,
)

async def cmd_start(update: Update, context: ContextTypes.DEFAULT_TYPE):
    await update.message.reply_text(
        "🦊 **Chào đại ca! Em là Harness Bot xịn xò đây.**\n\n"
        "Em giúp anh điều khiển AI Developer Harness trực tiếp qua Telegram.\n"
        "👉 Gõ `/help` để xem danh sách lệnh hoặc `/flags` để xem các tùy chọn chạy.\n"
        "🚀 Thử ngay: `/run --mock Kiểm tra dự án`"
    )

async def cmd_flags(update: Update, context: ContextTypes.DEFAULT_TYPE):
    keyboard = InlineKeyboardMarkup([
        [
            InlineKeyboardButton("--mock", callback_data="flag_mock"),
            InlineKeyboardButton("--auto-approve", callback_data="flag_auto"),
            InlineKeyboardButton("--skip-enricher", callback_data="flag_skip"),
        ]
    ])
    await update.message.reply_text(
        "🛠 **Danh sách Flags cho lệnh `/run`:**\n\n"
        "Bấm vào nút bên dưới để copy flag hoặc xem chi tiết:",
        reply_markup=keyboard
    )

async def cmd_run(update: Update, context: ContextTypes.DEFAULT_TYPE):
    if context.chat_data.get("running"):
        await update.message.reply_text(
            "⚠️ Đang có pipeline chạy. Dùng /cancel để hủy trước."
        )
        return

    text = update.message.text
    # Nếu người dùng gõ -- mà không có task, gợi ý flags
    if text.strip().startswith("/run --") and len(context.args) == 0:
        await cmd_flags(update, context)
        return

    args = list(context.args)
    mock_mode = "--mock" in args
    auto_approve = "--auto-approve" in args
    skip_enricher = "--skip-enricher" in args
    args = [a for a in args if a not in ("--mock", "--auto-approve", "--skip-enricher")]
    task = " ".join(args)
    if not task:
        await update.message.reply_text(
            "Cách dùng: `/run [--mock] [--auto-approve] [--skip-enricher] <nhiệm vụ>`\n"
            "  `--mock`            chạy giả lập (không cần API key)\n"
            "  `--auto-approve`    tự động phê duyệt (bỏ qua HITL)\n"
            "  `--skip-enricher`   bỏ qua làm giàu prompt ở Pha 0\n"
            "Ví dụ: /run --mock --skip-enricher Thêm entity PostCategory"
        )
        return

    chat_id = update.effective_chat.id
    msg_queue = queue.Queue()
    context.chat_data["running"] = True
    context.chat_data["msg_queue"] = msg_queue

    flag_labels = []
    if mock_mode:
        flag_labels.append("mock")
    if auto_approve:
        flag_labels.append("auto-approve")
    if skip_enricher:
        flag_labels.append("skip-enricher")
    tag = f" [{', '.join(flag_labels)}]" if flag_labels else ""
    await update.message.reply_text(f"🚀 **Đang chạy{tag}:** `{task}`")

    thread = _start_pipeline_thread(
        chat_id=chat_id,
        msg_queue=msg_queue,
        context=context,
        task=task,
        auto_approve=auto_approve,
        force_mock=mock_mode,
        skip_enricher=skip_enricher
    )
    asyncio.create_task(
        stream_output(chat_id, msg_queue, context.application, context.chat_data, thread)
    )

async def cmd_cancel(update: Update, context: ContextTypes.DEFAULT_TYPE):
    if not context.chat_data.get("running"):
        await update.message.reply_text("⚪ Không có pipeline nào đang chạy.")
        return

    harness = _harness_store.get(update.effective_chat.id)
    if harness:
        harness._approval_val = False
        harness._approval_evt.set()

    context.chat_data["running"] = False
    msg_queue = context.chat_data.get("msg_queue")
    if msg_queue:
        msg_queue.put(("log", "🛑 **Đã yêu cầu hủy.** Pipeline sẽ dừng sau bước hiện tại."))

    await update.message.reply_text("🛑 Đã gửi yêu cầu hủy.")

async def cmd_status(update: Update, context: ContextTypes.DEFAULT_TYPE):
    running = context.chat_data.get("running", False)
    pending = context.chat_data.get("pending_approval", False)
    if running:
        status = "🟢 Đang chạy"
        if pending:
            status += " (⏳ chờ phê duyệt)"
    else:
        status = "⚪ Rảnh"
    await update.message.reply_text(f"📊 **Trạng thái:** {status}")

async def cmd_git(update: Update, context: ContextTypes.DEFAULT_TYPE):
    if not context.args:
        await update.message.reply_text(
            "Cách dùng: `/git <lệnh>`\n"
            "Ví dụ:\n"
            "`/git status`\n"
            "`/git diff`\n"
            "`/git add .`\n"
            "`/git commit -m \"fix: sửa lỗi\"`\n"
            "`/git push`\n"
            "`/git log --oneline -5`\n"
            "`/git pull`"
        )
        return

    cmd_parts = " ".join(context.args)
    try:
        result = subprocess.run(
            f"git {cmd_parts}",
            shell=True,
            capture_output=True,
            encoding="utf-8",
            errors="replace",
            cwd=PROJECT_ROOT,
            timeout=60,
        )
        output = result.stdout or ""
        if result.stderr:
            output += "\n--- stderr ---\n" + result.stderr
        if not output.strip():
            output = "(không có output)"
        if len(output) > 3900:
            output = output[:3900] + "\n...(truncated)"
        await update.message.reply_text(
            f"```\n$ git {cmd_parts}\n{output}\n```",
        )
    except subprocess.TimeoutExpired:
        await update.message.reply_text("⏰ Lệnh git bị timeout (60s).")
    except Exception as e:
        await update.message.reply_text(f"❌ Lỗi: `{e}`")

async def cmd_help(update: Update, context: ContextTypes.DEFAULT_TYPE):
    await update.message.reply_text(
        "🤖 **Harness Bot — Danh sách lệnh**\n\n"
        "**🚀 Pipeline:**\n"
        "`/run [--flags] <nhiệm vụ>` — Chạy pipeline AI\n"
        "  👉 Gõ `/flags` để xem các flag hỗ trợ\n"
        "  `--mock`          chế độ giả lập\n"
        "  `--auto-approve`  tự động phê duyệt\n"
        "  `--skip-enricher`   bỏ qua làm giàu prompt\n"
        "`/cancel` — Hủy pipeline đang chạy\n"
        "`/status` — Xem trạng thái hiện tại\n\n"
        "**⚡ Skills:**\n"
        "`/skills` — Liệt kê toàn bộ skills đã cài đặt\n"
        "`/skill [tên]` — Kích hoạt skill (menu nếu không truyền tên)\n\n"
        "**⚙️ Cấu hình:**\n"
        "`/config` — Xem cấu hình `harness_config.json`\n"
        "`/log` — Tải file log phiên gần nhất\n\n"
        "**📦 Git:**\n"
        "`/git <lệnh>` — Chạy lệnh git\n\n"
        "**Ví dụ:**\n"
        "`/run --mock Thêm entity PostCategory`\n"
        "`/flags` ➡️ chọn flag ➡️ gõ lệnh\n"
        "`/skill optimizing-ef-core-queries`\n"
        "`/git log --oneline -5`"
    )
