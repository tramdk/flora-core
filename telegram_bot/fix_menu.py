import asyncio
import os
from telegram import Bot
from dotenv import load_dotenv

async def main():
    load_dotenv()
    token = os.getenv("TELEGRAM_BOT_TOKEN", "")
    if not token:
        print("Error: No TELEGRAM_BOT_TOKEN found in .env")
        return

    bot = Bot(token=token)
    commands = [
        ("start", "Start and welcome"),
        ("run", "Run AI pipeline (e.g. /run --mock Fix bug)"),
        ("flags", "Show available flags"),
        ("status", "Check bot status"),
        ("cancel", "Cancel running pipeline"),
        ("skill", "Activate specific skill"),
        ("skills", "List all skills"),
        ("git", "Run git command (e.g. /git status)"),
        ("help", "User guide"),
    ]
    
    try:
        await bot.set_my_commands(commands)
        print("SUCCESS: Telegram server menu updated!")
    except Exception as e:
        print(f"Error updating menu: {e}")

if __name__ == "__main__":
    asyncio.run(main())
