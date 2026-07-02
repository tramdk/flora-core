import asyncio
import io
import logging
import os
import queue
import sys
import threading
import traceback
from pathlib import Path
from telegram import Update, InlineKeyboardButton, InlineKeyboardMarkup
from telegram.ext import Application, ContextTypes
from telegram_bot.state import _harness_store

PROJECT_ROOT = Path(__file__).resolve().parent.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from scripts.ai_developer_harness import AIDeveloperHarness

logger = logging.getLogger(__name__)

# Patterns that are too noisy / redundant to forward to Telegram
_NOISE_PATTERNS = [
    # dotnet build noise
    "Determining projects",
    "Restore completed",
    "Build succeeded.",
    "Time Elapsed",
    # Section header labels — replaced by send_section() on Telegram
    "BẢN KẾ HOẠCH THỰC THI",
    "TESTWRITER AGENT",
    "DEVELOPER AGENT",
    "THÔNG BÁO TỪ",
    "PROPOSED EXECUTION PLAN",
]

class OutputCapture:
    """Capture stdout and stream to Telegram with noise filtering."""
    def __init__(self, msg_queue: queue.Queue):
        self.msg_queue = msg_queue
        self.buffer = ""

    def _is_noise(self, text: str) -> bool:
        stripped = text.strip()
        if not stripped:
            return True
        # Pure progress tick lines (dots, stars, spaces)
        if all(c in ".·* " for c in stripped):
            return True
        # Separator / divider lines  (e.g. ===...===, ---...---, ─────)
        # Drop any line where > 80% of non-space chars are the same separator char
        for sep in ("=", "-", "─", "—", "*", "#"):
            count = stripped.count(sep)
            if count >= 6 and count / max(len(stripped), 1) >= 0.8:
                return True
        for pat in _NOISE_PATTERNS:
            if pat in text:
                return True
        return False

    def write(self, text):
        if self._is_noise(text):
            return
        self.buffer += text
        # Flush when buffer is large enough OR at a complete line boundary
        if len(self.buffer) >= 800 or (text.endswith(("\n", "\r\n")) and len(self.buffer) >= 200):
            self.msg_queue.put(("log", self.buffer.rstrip()))
            self.buffer = ""

    def flush(self):
        if self.buffer.strip():
            self.msg_queue.put(("log", self.buffer.rstrip()))
            self.buffer = ""

class TelegramHarness(AIDeveloperHarness):
    """Harness with async-aware approval mechanism and rich Telegram messaging."""
    def __init__(self, msg_queue: queue.Queue, chat_id: int, auto_approve: bool = True, force_mock: bool | None = None):
        self._msg_queue = msg_queue
        self._chat_id = chat_id
        # Bridge to allow the sync harness to communicate with async callbacks
        self._approval_event = threading.Event()
        self._approval_val = False
        super().__init__(auto_approve=auto_approve)
        if force_mock:
            self.mock_mode = True
            self.llm_router.mock_mode = True

    # ── Messaging helpers ──────────────────────────────────────────────────

    def send_plan(self, plan_text: str, filename: str = "execution_plan.txt", caption: str = None):
        """Send execution plan as a downloadable .txt file to Telegram."""
        caption = caption or "📋 Bản kế hoạch thực thi — đọc và phê duyệt bên dưới."
        self._msg_queue.put(("plan", (plan_text, filename, caption)))

    def send_section(self, phase_num: int, phase_title: str):
        """Send a phase divider to Telegram."""
        self._msg_queue.put(("section", (phase_num, phase_title)))

    def send_log(self, message: str):
        """Send a plain log message to Telegram."""
        self._msg_queue.put(("log", message))

    # ── HITL approval ──────────────────────────────────────────────────────

    def ask_approval(self, message: str, force_ask: bool = False) -> bool:
        if self.auto_approve:
            return True

        # IMPORTANT: clear the event BEFORE putting message in queue.
        # If we clear AFTER, a fast callback could set() the event before
        # we clear it, causing an infinite wait (race condition).
        self._approval_event.clear()
        self._approval_val = False

        # Push approval request to Telegram (AFTER clearing the event)
        self._msg_queue.put(("approval", (message, self._chat_id)))
        logger.info(f"[ask_approval] Waiting for approval from chat {self._chat_id}...")

        # Block current thread until callback_handler calls event.set()
        signaled = self._approval_event.wait(timeout=600)

        if not signaled:
            logger.error(f"[ask_approval] Timed out waiting for chat {self._chat_id}")
            self._msg_queue.put(("log", "⚠️ Approval timeout sau 10 phút. Mặc định TỪ CHỐI."))
            return False

        logger.info(f"[ask_approval] Got approval={self._approval_val} from chat {self._chat_id}")
        return self._approval_val

async def _safe_send(bot, chat_id: int, text: str, reply_markup=None):
    import re as _re
    for attempt in range(3):
        try:
            await bot.send_message(chat_id=chat_id, text=text, disable_web_page_preview=True, reply_markup=reply_markup)
            return
        except Exception as exc:
            err = str(exc).lower()
            if "retry after" in err:
                m = _re.search(r"retry after\s+(\dig+)", err)
                await asyncio.sleep(int(m.group(1)) + 1 if m else 5)
            elif attempt < 2:
                await asyncio.sleep(1)
            else:
                logger.error("Send failed: %s", exc)

async def _safe_send_document(bot, chat_id: int, file_data: bytes, filename: str, caption: str = None):
    for attempt in range(3):
        try:
            await bot.send_document(chat_id=chat_id, document=io.BytesIO(file_data), filename=filename, caption=caption)
            return
        except Exception as exc:
            await asyncio.sleep(1 if attempt < 2 else 0)

async def stream_output(chat_id: int, msg_queue: queue.Queue, app: Application, chat_data: dict, harness_thread: threading.Thread):
    MESSAGE_GAP = 0.35

    def _get_item():
        """Non-blocking queue read — runs in a thread pool so the event loop stays free."""
        try:
            return msg_queue.get(timeout=0.5)
        except queue.Empty:
            return queue.Empty  # sentinel

    while harness_thread.is_alive() or not msg_queue.empty():
        # asyncio.to_thread keeps the event loop free to handle callback_query updates
        # (approve/reject buttons) while the harness thread is generating output.
        item = await asyncio.to_thread(_get_item)
        if item is queue.Empty:
            continue
        if item is None:
            break

        typ, payload = item

        # ── Plain log line ──────────────────────────────────────────────────
        if typ == "log":
            text = payload.strip()
            if not text:
                continue
            if len(text) > 1500:
                await _safe_send_document(
                    app.bot, chat_id,
                    text.encode("utf-8"), "harness_log.txt",
                    "📄 Output quá dài — xem file đính kèm"
                )
            else:
                await _safe_send(app.bot, chat_id, text)
            await asyncio.sleep(MESSAGE_GAP)

        # ── Phase / section divider ─────────────────────────────────────────
        elif typ == "section":
            phase_num, phase_title = payload
            icons = {1: "🗺", 2: "🧪", 3: "⚙️", 4: "✅", 5: "🔍"}
            icon = icons.get(phase_num, "📌")
            text = f"\n{icon} Pha {phase_num} — {phase_title}\n{'─' * 30}"
            await _safe_send(app.bot, chat_id, text)
            await asyncio.sleep(MESSAGE_GAP)

        # ── Execution plan → gửi dưới dạng file .txt ───────────────────────
        elif typ == "plan":
            plan_text, filename, caption = payload
            await _safe_send_document(
                app.bot, chat_id,
                plan_text.encode("utf-8"),
                filename,
                caption
            )
            await asyncio.sleep(MESSAGE_GAP)

        # ── Approval keyboard ───────────────────────────────────────────────
        elif typ == "approval":
            message, _chat_id = payload
            keyboard = InlineKeyboardMarkup([
                [
                    InlineKeyboardButton("✅ Đồng ý", callback_data="approve_yes"),
                    InlineKeyboardButton("❌ Từ chối", callback_data="approve_no"),
                ]
            ])
            chat_data["pending_approval"] = True
            formatted = (
                "🛡 Cần phê duyệt\n"
                + "─" * 30 + "\n"
                + message + "\n\n"
                + "Nhấn nút bên dưới để tiếp tục hoặc từ chối."
            )
            await _safe_send(app.bot, chat_id=_chat_id, text=formatted, reply_markup=keyboard)
            logger.info(f"[stream_output] Approval keyboard sent to chat {_chat_id}")

    chat_data["running"] = False
    chat_data["pending_approval"] = False
    await _safe_send(
        app.bot, chat_id,
        "✅ Pipeline hoàn tất!\n"
        "Kiểm tra kết quả: /git status hoặc /git diff"
    )

def _start_pipeline_thread(chat_id, msg_queue, context, task, auto_approve=True, force_mock=None, skip_enricher=False):
    def _run():
        old_stdout = sys.stdout
        sys.stdout = OutputCapture(msg_queue)
        harness = TelegramHarness(msg_queue=msg_queue, chat_id=chat_id, auto_approve=auto_approve, force_mock=force_mock)
        _harness_store[chat_id] = harness
        try:
            harness.execute_pipeline(task, skip_enricher=skip_enricher)
        except Exception as exc:
            msg_queue.put(("log", f"❌ **ERROR:** {exc}\n{traceback.format_exc()}"))
        finally:
            _harness_store.pop(chat_id, None)
            sys.stdout = old_stdout
            msg_queue.put(None)
    thread = threading.Thread(target=_run, daemon=True)
    context.chat_data["harness_thread"] = thread
    thread.start()
    return thread
