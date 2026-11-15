"""Send test items through Archipelago's supported text-client API."""

import asyncio
import json
import sys
from pathlib import Path

AP_ROOT = Path(__file__).resolve().parent / "archipelago-source" / "Archipelago-main"
sys.path.insert(0, str(AP_ROOT))

from CommonClient import CommonContext, server_loop  # noqa: E402


class AdminTextContext(CommonContext):
    tags = CommonContext.tags | {"TextOnly"}
    game = ""
    items_handling = 0
    want_slot_data = False

    def __init__(self, address: str, slot: str) -> None:
        super().__init__(address)
        self.auth = slot
        self.connected = asyncio.Event()
        self.packets = asyncio.Queue()

    async def server_auth(self, password_requested: bool = False) -> None:
        await self.send_connect(game="")

    def on_package(self, cmd: str, args: dict) -> None:
        self.packets.put_nowait((cmd, args))
        if cmd in {"Connected", "ConnectionRefused"}:
            self.connected.set()


async def main() -> int:
    if len(sys.argv) < 4:
        print("Usage: ap_admin_commands.py <host:port> <slot> <admin-password> [server-command ...]", file=sys.stderr)
        return 1

    address, slot, admin_password = sys.argv[1:4]
    commands = sys.argv[4:]
    context = AdminTextContext(address, slot)
    context.server_task = asyncio.create_task(server_loop(context), name="integration server loop")
    try:
        await asyncio.wait_for(context.connected.wait(), timeout=20)
        if context.slot is None:
            raise RuntimeError("Admin text connection was refused.")
        print("Admin text client connected.")

        async def command(text: str, expected: str) -> None:
            print(f"> {text}")
            await context.send_msgs([{"cmd": "Say", "text": text}])
            while True:
                cmd, args = await asyncio.wait_for(context.packets.get(), timeout=10)
                print(json.dumps({"cmd": cmd, **args}, ensure_ascii=False))
                if expected in json.dumps(args, ensure_ascii=False):
                    return

        await command(f"!admin login {admin_password}", "Login successful")
        for server_command in commands:
            await command(f"!admin {server_command}", "Cheat console")
    finally:
        await context.shutdown()
    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))
