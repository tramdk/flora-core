import os
import subprocess
import shutil
import time

def selective_rollback(modified_files: set) -> str:
    """Khôi phục có chọn lọc: chỉ rollback production code, giữ nguyên test cases và docs.
    
    Tự động tạo git stash backup trước khi rollback để có thể khôi phục nếu cần
    (dùng `git stash pop` hoặc `git stash apply stash@{N}`).
    """
    try:
        # Safety net: git stash trước khi rollback
        stash_msg = f"harness-rollback-backup-{int(time.time())}"
        stash_result = subprocess.run(
            ["git", "stash", "push", "-m", stash_msg, "--keep-index"],
            shell=False,
            capture_output=True,
            encoding='utf-8',
            errors='replace'
        )
        stash_created = stash_result.returncode == 0 and "No local changes" not in (stash_result.stdout or "")
        if stash_created:
            print(f"💾 [Rollback Safety]: Đã tạo git stash backup: '{stash_msg}'. Dùng `git stash pop` để khôi phục nếu cần.")
            # Unstash ngay lập tức để giữ working tree (stash chỉ để backup)
            subprocess.run(
                ["git", "stash", "pop", "--quiet"],
                shell=False, capture_output=True
            )
        
        # Loại bỏ shell=True
        result = subprocess.run(
            ["git", "status", "--porcelain"],
            shell=False,
            capture_output=True,
            encoding='utf-8',
            errors='replace'
        )
        if result.returncode != 0:
            return "Không thể lấy trạng thái git."
            
        rolled_back_files = []
        for line in result.stdout.splitlines():
            line = line.strip()
            if not line:
                continue
            parts = line.split(maxsplit=1)
            if len(parts) < 2:
                continue
            status, filepath = parts[0], parts[1].strip()
            if filepath.startswith('"') and filepath.endswith('"'):
                filepath = filepath[1:-1]
                
            # Chỉ khôi phục các tệp tin được chỉnh sửa/tạo bởi chính harness trong phiên chạy này
            abs_filepath = os.path.abspath(filepath)
            if abs_filepath not in modified_files:
                continue
                
            normalized_path = filepath.lower().replace("\\", "/")
            # Bỏ qua tệp test, docs, và plans
            is_test_or_doc = (
                "tests" in normalized_path or 
                "test.cs" in normalized_path or 
                "docs/" in normalized_path or 
                "execution_plan.md" in normalized_path or
                "evaluation_report.md" in normalized_path or
                "harness_run.log" in normalized_path
            )
            
            if not is_test_or_doc:
                if status == "??" or status == "A":
                    if os.path.exists(filepath):
                        if os.path.isdir(filepath):
                            shutil.rmtree(filepath)
                        else:
                            os.remove(filepath)
                    rolled_back_files.append(f"Xóa tệp untracked: {filepath}")
                else:
                    # Loại bỏ shell=True
                    subprocess.run(
                        ["git", "checkout", "--", filepath],
                        shell=False,
                        capture_output=True
                    )
                    rolled_back_files.append(f"Khôi phục tệp modified: {filepath}")
                    
        if rolled_back_files:
            return "Đã khôi phục các tệp tin production:\n" + "\n".join(f"  - {f}" for f in rolled_back_files)
        return "Không phát hiện tệp tin production nào bị thay đổi cần khôi phục."
    except Exception as e:
        return f"Lỗi trong quá trình selective rollback: {str(e)}"
