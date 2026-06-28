import subprocess
import shlex
import sys

def run_dotnet_command(command: str, cwd: str = None) -> tuple[int, str]:
    """Chạy một lệnh dotnet CLI hoặc git và trả về exit code cùng console output.
    
    Args:
        command: Lệnh cần chạy (ví dụ: 'dotnet build')
        cwd: Thư mục làm việc (working directory). Nếu None, dùng CWD hiện tại.
    """
    # Chặn tấn công Command Injection bằng cách kiểm tra các ký tự nối lệnh shell
    forbidden_chars = [';', '&', '|', '`', '$', '\n', '\r']
    if any(char in command for char in forbidden_chars):
        return -3, "Lỗi bảo mật: Lệnh chứa ký tự cấm nối lệnh (Command Injection Guardrail)."
    try:
        print(f"\n[Harness Executing]: {command}" + (f" (cwd={cwd})" if cwd else ""))
        
        # Sửa P0: Loại bỏ shell=True để tránh rủi ro bảo mật
        posix_val = not sys.platform.startswith('win')
        cmd_args = shlex.split(command, posix=posix_val)
        
        result = subprocess.run(
            cmd_args,
            shell=False,
            capture_output=True,
            encoding='utf-8',
            errors='replace',
            timeout=300,  # Timeout 5 phút tránh treo đúp
            cwd=cwd
        )
        output = (result.stdout or "") + "\n" + (result.stderr or "")
        return result.returncode, output
    except subprocess.TimeoutExpired:
        return -1, "Lỗi: Lệnh bị Timeout (quá 5 phút)."
    except Exception as e:
        return -2, f"Lỗi không xác định: {str(e)}"
