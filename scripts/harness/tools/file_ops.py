import os
import subprocess
import shlex
import sys


def search_codebase(query: str, file_glob: str = "*.cs", max_results: int = 20) -> str:
    """Tìm kiếm pattern trong toàn bộ codebase bằng ripgrep (rg) hoặc fallback sang findstr/grep.
    
    Trả về danh sách kết quả: file_path:line_number:line_content (tối đa max_results).
    Giúp agent định vị symbol nhanh mà không cần đọc hết file.
    """
    # Xác định thư mục gốc dự án
    tools_dir = os.path.dirname(os.path.abspath(__file__))
    harness_dir = os.path.dirname(tools_dir)
    scripts_dir = os.path.dirname(harness_dir)
    root_dir = os.path.dirname(scripts_dir)
    
    try:
        # Ưu tiên ripgrep (rg) — nhanh nhất
        posix_val = not sys.platform.startswith('win')
        cmd = [
            "rg", "--no-heading", "--line-number", "--color=never",
            "--max-count", str(max_results),
            "-g", file_glob,
            "--", query, root_dir
        ]
        result = subprocess.run(
            cmd, shell=False, capture_output=True,
            encoding='utf-8', errors='replace', timeout=30
        )
        if result.returncode <= 1:  # rg returns 1 if no matches
            output = result.stdout.strip()
            if not output:
                return f"Không tìm thấy kết quả nào cho '{query}' trong {file_glob}."
            
            # Rút gọn absolute path thành relative path
            lines = output.splitlines()[:max_results]
            relative_lines = []
            for line in lines:
                try:
                    relative_lines.append(os.path.relpath(line.split(":", 1)[0], root_dir) + ":" + line.split(":", 1)[1])
                except Exception:
                    relative_lines.append(line)
            
            total = len(output.splitlines())
            result_text = "\n".join(relative_lines)
            if total > max_results:
                result_text += f"\n\n... (Hiển thị {max_results}/{total} kết quả. Thu hẹp query để xem thêm.)"
            return result_text
    except FileNotFoundError:
        pass  # rg chưa cài, fallback
    except Exception:
        pass
    
    # Fallback: dùng findstr (Windows) hoặc grep (Unix)
    try:
        if sys.platform.startswith('win'):
            cmd = ["findstr", "/s", "/n", "/i", query, os.path.join(root_dir, "**", file_glob)]
        else:
            cmd = ["grep", "-rnI", "--include=" + file_glob, query, root_dir]
        
        result = subprocess.run(
            cmd, shell=False, capture_output=True,
            encoding='utf-8', errors='replace', timeout=30
        )
        output = result.stdout.strip()
        if not output:
            return f"Không tìm thấy kết quả nào cho '{query}' trong {file_glob}."
        
        lines = output.splitlines()[:max_results]
        return "\n".join(lines)
    except Exception as e:
        return f"Lỗi tìm kiếm: {str(e)}"


def patch_source(file_path: str, search_text: str, replace_text: str) -> str:
    """Thay thế một đoạn text cụ thể trong file (surgical diff-based edit).
    
    Thay vì ghi đè toàn bộ file (write_source), chỉ tìm và thay thế chính xác
    đoạn search_text bằng replace_text. Tiết kiệm token đáng kể cho agent.
    
    Returns:
        Thông báo kết quả (thành công/lỗi).
    """
    try:
        abs_path = os.path.abspath(file_path)
        if not os.path.exists(abs_path):
            return f"Lỗi: File '{file_path}' không tồn tại. Dùng write_source để tạo file mới."
        
        with open(abs_path, 'r', encoding='utf-8') as f:
            content = f.read()
        
        # Kiểm tra search_text có tồn tại trong file không
        occurrences = content.count(search_text)
        if occurrences == 0:
            # Gợi ý: cho agent biết nội dung gần đúng nhất để tự sửa
            preview = content[:500]
            return (
                f"Lỗi: Không tìm thấy đoạn text cần thay thế trong '{file_path}'.\n"
                f"Hãy dùng view_source để đọc lại file, sau đó copy chính xác đoạn text cần sửa.\n"
                f"Preview 500 ký tự đầu file:\n{preview}"
            )
        
        if occurrences > 1:
            return (
                f"Cảnh báo: Tìm thấy {occurrences} lần xuất hiện của đoạn text trong '{file_path}'.\n"
                f"patch_source chỉ thay thế lần xuất hiện ĐẦU TIÊN.\n"
                f"Nếu muốn thay thế tất cả, gọi patch_source nhiều lần hoặc dùng write_source."
            )
        
        # Thực hiện thay thế (chỉ lần xuất hiện đầu tiên)
        new_content = content.replace(search_text, replace_text, 1)
        
        # Xử lý thuộc tính Read-Only
        if os.path.exists(abs_path):
            try:
                import stat
                current_mode = os.stat(abs_path).st_mode
                if not (current_mode & stat.S_IWRITE):
                    os.chmod(abs_path, current_mode | stat.S_IWRITE)
            except Exception:
                pass
        
        with open(abs_path, 'w', encoding='utf-8') as f:
            f.write(new_content)
        
        return "Patch thành công."
    except PermissionError as pe:
        return f"Lỗi patch file: Truy cập bị từ chối (Permission Denied). Chi tiết: {str(pe)}"
    except Exception as e:
        return f"Lỗi patch file: {str(e)}"


def read_source_file(file_path: str, start_line: int = None, end_line: int = None) -> str:
    """Đọc nội dung một file nguồn trong dự án (hỗ trợ phân trang)."""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()
            
            if start_line is not None and end_line is not None:
                # 1-indexed for user input
                start = max(0, start_line - 1)
                end = min(len(lines), end_line)
                lines = lines[start:end]
                
            return "".join(lines)
    except Exception as e:
        return f"Lỗi đọc file: {str(e)}"

def write_source_file(file_path: str, content: str) -> str:
    """Ghi nội dung mới vào một file nguồn."""
    try:
        # Resolve về absolute path để tránh CWD-dependent
        abs_path = os.path.abspath(file_path)
        dir_name = os.path.dirname(abs_path)
        if dir_name:
            os.makedirs(dir_name, exist_ok=True)
            
        # Xử lý thuộc tính Read-Only trên Windows/Linux trước khi ghi
        if os.path.exists(abs_path):
            try:
                import stat
                current_mode = os.stat(abs_path).st_mode
                # Nếu file bị đánh dấu read-only, xóa cờ này đi
                if not (current_mode & stat.S_IWRITE):
                    os.chmod(abs_path, current_mode | stat.S_IWRITE)
            except Exception as stat_err:
                # Bỏ qua nếu không có quyền set chmod, thử ghi tiếp
                pass
                
        with open(abs_path, 'w', encoding='utf-8') as f:
            f.write(content)
        if not os.path.exists(abs_path):
            return f"Lỗi ghi file: {abs_path} không tồn tại sau khi ghi (có thể bị redirect/block)."
        return "Ghi file thành công."
    except PermissionError as pe:
        return f"Lỗi ghi file: Truy cập bị từ chối (Permission Denied). File có thể đang bị khóa (lock) bởi tiến trình khác (như dotnet build, IDE, hoặc Git) hoặc thuộc tính bảo mật của hệ điều hành. Chi tiết: {str(pe)}"
    except Exception as e:
        return f"Lỗi ghi file: {str(e)}"

