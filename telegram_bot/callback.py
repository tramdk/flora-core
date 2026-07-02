import asyncio
import queue
import logging
from telegram import Update
from telegram.ext import ContextTypes
from telegram_bot.state import _harness_store

logger = logging.getLogger(__name__)

async def callback_handler(update: Update, context: ContextTypes.DEFAULT_TYPE):
    query = update.callback_query
    if not query:
        return
    
    logger.info(f"CALLBACK RECEIVED: {query.data}")
    await query.answer()
    data = query.data


    if data.startswith("approve_"):
        result = data == "approve_yes"
        chat_id = update.effective_chat.id
        logger.info(f"Processing approval for chat {chat_id}, value: {result}")
        
        harness = _harness_store.get(chat_id)
        if harness:
            try:
                harness._approval_val = result
                harness._approval_event.set()
                logger.info(f"Successfully signaled harness for chat {chat_id}")
            except Exception as e:
                logger.error(f"Failed to set approval event for chat {chat_id}: {e}", exc_info=True)
        else:
            logger.error(f"No active harness found in store for chat {chat_id}")
        
        context.chat_data["pending_approval"] = False
        await query.edit_message_text(f"{'✅' if result else '❌'} Decision recorded.")

    elif data.startswith("flag_"):
        flag_map = {"flag_mock": "--mock", "flag_auto": "--auto-approve", "flag_skip": "--skip-enricher"}
        flag_text = flag_map.get(data, "Unknown flag")
        await query.edit_message_text(f"💡 Use: `/run {flag_text} <task>`")

    elif data.startswith("skill_"):
        skill_name = data[len("skill_"):]
        if context.chat_data.get("running"):
            await query.edit_message_text("⚠️ Pipeline already running.")
            return
        
        from telegram_bot.harness_bridge import _start_pipeline_thread, stream_output
        task = f"Expert analysis and optimization for skill: {skill_name}"
        chat_id = update.effective_chat.id
        msg_queue = queue.Queue()
        context.chat_data["running"] = True
        context.chat_data["msg_queue"] = msg_queue
        thread = _start_pipeline_thread(chat_id, msg_queue, context, task)
        asyncio.create_task(stream_output(chat_id, msg_queue, context.application, context.chat_data, thread))
