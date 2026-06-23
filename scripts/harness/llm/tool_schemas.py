"""
Định nghĩa JSON Schema cho các công cụ (Tools) sử dụng trong Native Function Calling.
Sử dụng định dạng chuẩn của OpenAI/Deepseek, các provider khác như Gemini/Claude
sẽ được translate lại trong router.py.
"""

TOOLS_SCHEMA = [
    {
        "type": "function",
        "function": {
            "name": "execute_command",
            "description": "Thực thi lệnh shell (ví dụ: dotnet build, dotnet test, git status, git diff, git add). BẮT BUỘC dùng cho lệnh hệ thống. Tuyệt đối không dùng git commit.",
            "parameters": {
                "type": "object",
                "properties": {
                    "command": {
                        "type": "string",
                        "description": "Lệnh cần chạy (ví dụ: 'dotnet test')"
                    }
                },
                "required": ["command"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "view_source",
            "description": "Đọc nội dung một tệp mã nguồn. Hỗ trợ phân trang để đọc các tệp tin dài.",
            "parameters": {
                "type": "object",
                "properties": {
                    "file_path": {
                        "type": "string",
                        "description": "Đường dẫn tuyệt đối hoặc tương đối tới file cần đọc"
                    },
                    "start_line": {
                        "type": "integer",
                        "description": "Dòng bắt đầu đọc (1-indexed). Bỏ trống nếu muốn đọc toàn bộ file (không khuyến khích với file lớn)."
                    },
                    "end_line": {
                        "type": "integer",
                        "description": "Dòng kết thúc đọc. Bỏ trống nếu muốn đọc toàn bộ file."
                    }
                },
                "required": ["file_path"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "write_source",
            "description": "Ghi đè nội dung mới vào một tệp mã nguồn. Cần cung cấp toàn bộ nội dung file.",
            "parameters": {
                "type": "object",
                "properties": {
                    "file_path": {
                        "type": "string",
                        "description": "Đường dẫn tới file cần ghi"
                    },
                    "content": {
                        "type": "string",
                        "description": "Nội dung hoàn chỉnh sẽ ghi đè vào file"
                    }
                },
                "required": ["file_path", "content"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "finish_task",
            "description": "Kết thúc lượt làm việc của Agent, gửi kết quả báo cáo cho Evaluator hoặc hoàn thành nhiệm vụ.",
            "parameters": {
                "type": "object",
                "properties": {
                    "summary": {
                        "type": "string",
                        "description": "Báo cáo chi tiết về những gì đã làm, kết quả đạt được."
                    }
                },
                "required": ["summary"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "search_codebase",
            "description": "Tìm kiếm nhanh một pattern (tên class, method, property, string) trong toàn bộ codebase. Trả về danh sách file:dòng:nội_dung. Tiết kiệm token hơn nhiều so với đọc từng file bằng view_source.",
            "parameters": {
                "type": "object",
                "properties": {
                    "query": {
                        "type": "string",
                        "description": "Pattern cần tìm (ví dụ: 'IProductRepository', 'ProcessPaymentAsync', 'class Order')"
                    },
                    "file_glob": {
                        "type": "string",
                        "description": "Glob lọc file (mặc định: '*.cs'). Ví dụ: '*.cs', '*.json', '*.md'"
                    }
                },
                "required": ["query"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "patch_source",
            "description": "Sửa đổi phẫu thuật (surgical edit): Tìm một đoạn text chính xác trong file và thay thế bằng nội dung mới. TIẾT KIỆM TOKEN hơn write_source vì không cần gửi toàn bộ nội dung file. Chỉ dùng khi file đã tồn tại và bạn chỉ muốn sửa một phần nhỏ.",
            "parameters": {
                "type": "object",
                "properties": {
                    "file_path": {
                        "type": "string",
                        "description": "Đường dẫn tới file cần sửa"
                    },
                    "search_text": {
                        "type": "string",
                        "description": "Đoạn text CHÍNH XÁC cần tìm và thay thế (copy từ view_source output)"
                    },
                    "replace_text": {
                        "type": "string",
                        "description": "Nội dung mới thay thế cho search_text"
                    }
                },
                "required": ["file_path", "search_text", "replace_text"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "codegraph_query",
            "description": "Tìm kiếm định nghĩa lớp, phương thức, thuộc tính hoặc tệp tin trong toàn bộ codebase bằng CodeGraph.",
            "parameters": {
                "type": "object",
                "properties": {
                    "search": {
                        "type": "string",
                        "description": "Từ khóa tìm kiếm symbol (ví dụ: 'PaymentService', 'ProcessPaymentAsync')"
                    }
                },
                "required": ["search"]
            }
        }
    },
    {
        "type": "function",
        "function": {
            "name": "codegraph_get_context",
            "description": "Thu thập toàn bộ ngữ cảnh, tệp tin và cấu trúc liên quan đến một tác vụ kỹ thuật cụ thể bằng CodeGraph.",
            "parameters": {
                "type": "object",
                "properties": {
                    "task": {
                        "type": "string",
                        "description": "Mô tả tác vụ kỹ thuật (ví dụ: 'tạo API thanh toán qua ZaloPay')"
                    }
                },
                "required": ["task"]
            }
        }
    }
]

