"""
LLM Router với Native Function Calling (Tool Use).
Hỗ trợ Gemini, OpenAI, Claude, DeepSeek.
Chuyển đổi từ Regex-based parsing sang chuẩn Function Calling API.
"""
import os
import sys
import time
import json
import re
import copy


class LLMRouter:
    def __init__(self, provider: str, api_key: str, model_name: str, log_file: str, mock_mode: bool, on_token_usage=None):
        self.provider = provider
        self.api_key = api_key
        self.model_name = model_name
        self.log_file = log_file
        self.mock_mode = mock_mode
        self.on_token_usage = on_token_usage
        self.gemini_caches = {}
        self.client = None
        self.current_role = "Planner"
        
        if not self.mock_mode:
            self.init_llm_client()
            
    def init_llm_client(self):
        """Khởi tạo Client LLM dựa trên Provider."""
        try:
            if self.provider == "gemini":
                from google import genai
                from google.genai import types
                self.client = genai.Client(
                    api_key=self.api_key,
                    http_options=types.HttpOptions(timeout=600_000) # Tăng timeout lên 10 phút
                )
            elif self.provider == "openai":
                from openai import OpenAI
                self.client = OpenAI(api_key=self.api_key)
            elif self.provider == "claude":
                import anthropic
                self.client = anthropic.Anthropic(api_key=self.api_key)
            elif self.provider == "deepseek":
                from openai import OpenAI
                self.client = OpenAI(api_key=self.api_key, base_url="https://api.deepseek.com")
        except ImportError as e:
            missing_lib = {
                "gemini": "google-genai",
                "openai": "openai",
                "claude": "anthropic",
                "deepseek": "openai"
            }.get(self.provider, "openai")
            print(f"\n[LỖI HỆ THỐNG]: Chưa cài đặt thư viện '{missing_lib}'.")
            print(f"Vui lòng chạy lệnh sau trên terminal để cài đặt:\n👉 pip install {missing_lib}\n")
            sys.exit(1)

    # =========================================================================
    # CHUYỂN ĐỔI TOOL SCHEMA CHO TỪNG PROVIDER
    # =========================================================================

    def _convert_tools_for_gemini(self, tools_schema: list[dict]) -> list:
        """Chuyển đổi OpenAI-format tools sang Gemini FunctionDeclaration."""
        from google.genai import types
        
        declarations = []
        for tool in tools_schema:
            func = tool["function"]
            # Gemini yêu cầu Schema object, chuyển từ JSON Schema sang
            params_schema = func.get("parameters", {})
            
            declarations.append(types.FunctionDeclaration(
                name=func["name"],
                description=func["description"],
                parameters=params_schema
            ))
        return [types.Tool(function_declarations=declarations)]

    def _convert_tools_for_claude(self, tools_schema: list[dict]) -> list[dict]:
        """Chuyển đổi OpenAI-format tools sang Claude tool format."""
        claude_tools = []
        for tool in tools_schema:
            func = tool["function"]
            claude_tools.append({
                "name": func["name"],
                "description": func["description"],
                "input_schema": func.get("parameters", {"type": "object", "properties": {}})
            })
        return claude_tools

    # =========================================================================
    # CHUYỂN ĐỔI MESSAGES CHO TỪNG PROVIDER
    # =========================================================================

    def _convert_messages_for_gemini(self, messages: list[dict]) -> list:
        """Chuyển đổi messages format chuẩn sang Gemini Content objects."""
        from google.genai import types
        
        contents = []
        for msg in messages:
            role = msg["role"]
            
            if role == "system":
                # System messages sẽ được xử lý riêng qua system_instruction
                continue
            elif role == "user":
                contents.append(types.Content(
                    role="user",
                    parts=[types.Part.from_text(text=msg["content"])]
                ))
            elif role == "assistant":
                parts = []
                # Text content (thought)
                if msg.get("content"):
                    parts.append(types.Part.from_text(text=msg["content"]))
                # Tool calls
                if msg.get("tool_calls"):
                    for tc in msg["tool_calls"]:
                        parts.append(types.Part.from_function_call(
                            name=tc["function"]["name"],
                            args=json.loads(tc["function"]["arguments"]) if isinstance(tc["function"]["arguments"], str) else tc["function"]["arguments"]
                        ))
                if parts:
                    contents.append(types.Content(role="model", parts=parts))
            elif role == "tool":
                # OpenAI format uses 'name' for the function name in role='tool' message, or tool_call_id
                function_name = msg.get("name") or "unknown"
                contents.append(types.Content(
                    role="user",
                    parts=[types.Part.from_function_response(
                        name=function_name,
                        response={"result": msg["content"]}
                    )]
                ))
        return contents

    def _convert_messages_for_claude(self, messages: list[dict]) -> list[dict]:
        """Chuyển đổi messages format chuẩn sang Claude format."""
        claude_messages = []
        
        for msg in messages:
            role = msg["role"]
            
            if role == "system":
                continue  # Handled separately via system parameter
            elif role == "user":
                claude_messages.append({
                    "role": "user",
                    "content": msg["content"]
                })
            elif role == "assistant":
                content_blocks = []
                if msg.get("content"):
                    content_blocks.append({"type": "text", "text": msg["content"]})
                if msg.get("tool_calls"):
                    for tc in msg["tool_calls"]:
                        args = tc["function"]["arguments"]
                        if isinstance(args, str):
                            args = json.loads(args)
                        content_blocks.append({
                            "type": "tool_use",
                            "id": tc["id"],
                            "name": tc["function"]["name"],
                            "input": args
                        })
                claude_messages.append({"role": "assistant", "content": content_blocks})
            elif role == "tool":
                claude_messages.append({
                    "role": "user",
                    "content": [{
                        "type": "tool_result",
                        "tool_use_id": msg.get("tool_call_id", "unknown"),
                        "content": msg["content"]
                    }]
                })
        return claude_messages

    # =========================================================================
    # CHUẨN HÓA RESPONSE TỪ TỪNG PROVIDER
    # =========================================================================

    def _normalize_gemini_response(self, response) -> dict:
        """Chuẩn hóa response từ Gemini API sang định dạng thống nhất."""
        result = {"thought": "", "tool_calls": [], "raw_text": "", "usage": None}
        
        # Extract usage metadata
        usage = getattr(response, 'usage_metadata', None)
        if usage:
            result["usage"] = {
                "input_tokens": getattr(usage, 'prompt_token_count', 0),
                "output_tokens": getattr(usage, 'candidates_token_count', 0),
                "cached_tokens": getattr(usage, 'cached_content_token_count', 0),
            }
        
        candidate = response.candidates[0] if response.candidates else None
        if not candidate or not candidate.content or not candidate.content.parts:
            return result
            
        for part in candidate.content.parts:
            if hasattr(part, 'text') and part.text:
                result["thought"] += part.text
                result["raw_text"] += part.text
            elif hasattr(part, 'function_call') and part.function_call:
                fc = part.function_call
                args = dict(fc.args) if fc.args else {}
                result["tool_calls"].append({
                    "id": f"call_{fc.name}_{int(time.time())}",
                    "function": {
                        "name": fc.name,
                        "arguments": args
                    }
                })
        return result

    def _normalize_openai_response(self, response) -> dict:
        """Chuẩn hóa response từ OpenAI/DeepSeek API sang định dạng thống nhất."""
        result = {"thought": "", "tool_calls": [], "raw_text": "", "usage": None}
        
        # Extract usage metadata
        usage = getattr(response, 'usage', None)
        if usage:
            result["usage"] = {
                "input_tokens": getattr(usage, 'prompt_tokens', 0),
                "output_tokens": getattr(usage, 'completion_tokens', 0),
                "cached_tokens": 0,
            }
        
        choice = response.choices[0]
        message = choice.message
        
        if message.content:
            result["thought"] = message.content
            result["raw_text"] = message.content
            
        if message.tool_calls:
            for tc in message.tool_calls:
                args = tc.function.arguments
                if isinstance(args, str):
                    try:
                        args = json.loads(args)
                    except json.JSONDecodeError:
                        args = {"raw": args}
                result["tool_calls"].append({
                    "id": tc.id,
                    "function": {
                        "name": tc.function.name,
                        "arguments": args
                    }
                })
        return result

    def _normalize_claude_response(self, response) -> dict:
        """Chuẩn hóa response từ Claude API sang định dạng thống nhất."""
        result = {"thought": "", "tool_calls": [], "raw_text": "", "usage": None}
        
        # Extract usage metadata
        usage = getattr(response, 'usage', None)
        if usage:
            cache_read = getattr(usage, 'cache_read_input_tokens', 0) or 0
            result["usage"] = {
                "input_tokens": getattr(usage, 'input_tokens', 0),
                "output_tokens": getattr(usage, 'output_tokens', 0),
                "cached_tokens": cache_read,
            }
        
        for block in response.content:
            if block.type == "text":
                result["thought"] += block.text
                result["raw_text"] += block.text
            elif block.type == "tool_use":
                result["tool_calls"].append({
                    "id": block.id,
                    "function": {
                        "name": block.name,
                        "arguments": block.input
                    }
                })
        return result

    # =========================================================================
    # HÀM GỌI LLM CHÍNH (Native Function Calling)
    # =========================================================================

    def call_llm_with_tools(
        self,
        messages: list[dict],
        system_instruction: str,
        tools_schema: list[dict],
        build_cache_func=None
    ) -> dict:
        """
        Gọi LLM với Native Function Calling.
        
        Args:
            messages: Mảng messages theo chuẩn OpenAI format
                      [{"role": "user"|"assistant"|"tool", "content": "...", ...}]
            system_instruction: System prompt
            tools_schema: Danh sách tool schemas (OpenAI format)
            build_cache_func: Hàm tạo cache contents cho Gemini
            
        Returns:
            Dict chuẩn hóa: {"thought": str, "tool_calls": [{"id": str, "function": {"name": str, "arguments": dict}}], "raw_text": str}
        """
        if self.mock_mode:
            return self._get_mock_tool_response(self.current_role)
            
        max_retries = 3
        backoff = 2
        
        for attempt in range(max_retries):
            try:
                if self.provider == "gemini":
                    return self._call_gemini_with_tools(messages, system_instruction, tools_schema, build_cache_func)
                elif self.provider == "claude":
                    return self._call_claude_with_tools(messages, system_instruction, tools_schema)
                else:
                    # OpenAI / DeepSeek
                    return self._call_openai_with_tools(messages, system_instruction, tools_schema)
            except Exception as e:
                err_msg = str(e)
                is_rate_limit = any(keyword in err_msg or keyword in err_msg.lower() for keyword in ["429", "resource_exhausted", "quota", "rate limit", "rate_limit"])
                
                if attempt < max_retries - 1:
                    sleep_time = 15 * (attempt + 1) if is_rate_limit else backoff
                    print(f"⚠️ [Harness API Warning]: Lần gọi {attempt + 1} cho {self.provider.upper()} thất bại ({err_msg}). Đang thử lại sau {sleep_time} giây...")
                    time.sleep(sleep_time)
                    if not is_rate_limit:
                        backoff *= 2
                else:
                    print(f"\n=============================================================")
                    print(f"⚠️ [CẢNH BÁO LỖI KẾT NỐI API {self.provider.upper()}]")
                    print(f"=============================================================")
                    
                    if is_rate_limit:
                        print(f"👉 Chi tiết: API bị giới hạn tốc độ sau {max_retries} lần thử lại với Progressive Backoff.")
                    elif "429" in err_msg or "resource_exhausted" in err_msg.lower() or "credits are depleted" in err_msg.lower():
                        print(f"👉 Chi tiết: Hết hạn mức API. Vui lòng nạp thêm credits tại https://aistudio.google.com.")
                    elif "402" in err_msg or "insufficient_balance" in err_msg.lower():
                        print(f"👉 Chi tiết: Tài khoản API không đủ số dư (Insufficient Balance).")
                    elif "403" in err_msg or "401" in err_msg or "invalid" in err_msg.lower() or "unauthorized" in err_msg.lower():
                        print(f"👉 Chi tiết: Khóa API '{self.provider.upper()}_API_KEY' không hợp lệ.")
                    else:
                        print(f"👉 Chi tiết lỗi: {err_msg}")
                        
                    print(f"\n🤖 [Hệ thống]: Tự động chuyển đổi sang chế độ giả lập (Mock Mode) để tiếp tục quy trình...")
                    print(f"=============================================================\n")
                    self.mock_mode = True
                    return self._get_mock_tool_response(self.current_role)
    def _call_gemini_with_tools(self, messages, system_instruction, tools_schema, build_cache_func) -> dict:
        """Gọi Gemini API với Native Function Calling."""
        from google.genai import types
        
        active_model = self.model_name or "gemini-2.5-flash"
        
        model_id = active_model
        if not model_id.startswith("models/"):
            model_id = f"models/{model_id}"
            
        # Chuyển đổi tools và messages
        gemini_tools = self._convert_tools_for_gemini(tools_schema)
        gemini_contents = self._convert_messages_for_gemini(messages)
        
        config_kwargs = {
            "temperature": 0.0,
            "tools": gemini_tools,
            "system_instruction": system_instruction
        }
        
        response = self.client.models.generate_content(
            model=model_id,
            contents=gemini_contents,
            config=types.GenerateContentConfig(**config_kwargs)
        )
        
        # Log usage
        usage = getattr(response, 'usage_metadata', None)
        if usage:
            prompt_tokens = getattr(usage, 'prompt_token_count', 0)
            completion_tokens = getattr(usage, 'candidates_token_count', 0)
            cached_tokens = getattr(usage, 'cached_content_token_count', 0)
            total_tokens = getattr(usage, 'total_token_count', 0)
            log_msg = (
                f"\n📊 [GEMINI API USAGE]:\n"
                f"   ├─ 📥 Input Tokens: {prompt_tokens} ({cached_tokens} cached)\n"
                f"   ├─ 📤 Output Tokens: {completion_tokens}\n"
                f"   └─ 🧮 Total Tokens: {total_tokens}\n"
            )
            print(log_msg)
            with open(self.log_file, "a", encoding="utf-8") as f:
                f.write(log_msg)
            if self.on_token_usage:
                self.on_token_usage(prompt_tokens, completion_tokens, cached_tokens)
                
        return self._normalize_gemini_response(response)

    def _call_claude_with_tools(self, messages, system_instruction, tools_schema) -> dict:
        """Gọi Claude API với Native Tool Use."""
        claude_tools = self._convert_tools_for_claude(tools_schema)
        claude_messages = self._convert_messages_for_claude(messages)
        
        kwargs = {
            "model": self.model_name,
            "max_tokens": 8192,
            "temperature": 0.0,
            "system": [
                {
                    "type": "text",
                    "text": system_instruction,
                    "cache_control": {"type": "ephemeral"}
                }
            ],
            "messages": claude_messages,
            "tools": claude_tools,
            "extra_headers": {
                "anthropic-beta": "prompt-caching-2024-07-31"
            }
        }
        
        response = self.client.messages.create(**kwargs)
        
        # Log usage
        usage = getattr(response, 'usage', None)
        if usage:
            input_tokens = getattr(usage, 'input_tokens', 0)
            output_tokens = getattr(usage, 'output_tokens', 0)
            cache_read = getattr(usage, 'cache_read_input_tokens', 0) or 0
            cache_creation = getattr(usage, 'cache_creation_input_tokens', 0) or 0
            log_msg = (
                f"\n📊 [CLAUDE API USAGE]:\n"
                f"   ├─ 📥 Input Tokens: {input_tokens}\n"
                f"   ├─ 📤 Output Tokens: {output_tokens}\n"
                f"   ├─ ♻️  Cache Read (hit): {cache_read} tokens\n"
                f"   └─ 🆕 Cache Creation (miss): {cache_creation} tokens\n"
            )
            print(log_msg)
            with open(self.log_file, "a", encoding="utf-8") as f:
                f.write(log_msg)
            if self.on_token_usage:
                self.on_token_usage(input_tokens, output_tokens, cache_read)
                
        return self._normalize_claude_response(response)

    def _call_openai_with_tools(self, messages, system_instruction, tools_schema) -> dict:
        """Gọi OpenAI/DeepSeek API với Native Function Calling."""
        # Xây dựng messages list theo chuẩn OpenAI
        openai_messages = [{"role": "system", "content": system_instruction}]
        
        for msg in messages:
            role = msg["role"]
            if role == "system":
                continue
            elif role == "user":
                openai_messages.append({"role": "user", "content": msg["content"]})
            elif role == "assistant":
                assistant_msg = {}
                assistant_msg["role"] = "assistant"
                if msg.get("content"):
                    assistant_msg["content"] = msg["content"]
                if msg.get("tool_calls"):
                    # Chuyển đổi arguments từ dict sang JSON string nếu cần
                    serialized_calls = []
                    for tc in msg["tool_calls"]:
                        serialized_tc = copy.deepcopy(tc)
                        serialized_tc["type"] = "function"
                        if isinstance(serialized_tc["function"]["arguments"], dict):
                            serialized_tc["function"]["arguments"] = json.dumps(serialized_tc["function"]["arguments"])
                        serialized_calls.append(serialized_tc)
                    assistant_msg["tool_calls"] = serialized_calls
                openai_messages.append(assistant_msg)
            elif role == "tool":
                openai_messages.append({
                    "role": "tool",
                    "tool_call_id": msg.get("tool_call_id", "unknown"),
                    "content": msg["content"]
                })
        
        kwargs = {
            "model": self.model_name,
            "messages": openai_messages,
            "tools": tools_schema,
            "temperature": 0.0
        }
        
        response = self.client.chat.completions.create(**kwargs)
        
        # Log usage
        usage = getattr(response, 'usage', None)
        if usage:
            prompt_tokens = getattr(usage, 'prompt_tokens', 0)
            completion_tokens = getattr(usage, 'completion_tokens', 0)
            log_msg = (
                f"\n📊 [OPENAI/DEEPSEEK API USAGE]:\n"
                f"   ├─ 📥 Input Tokens: {prompt_tokens}\n"
                f"   └─ 📤 Output Tokens: {completion_tokens}\n"
            )
            print(log_msg)
            with open(self.log_file, "a", encoding="utf-8") as f:
                f.write(log_msg)
            if self.on_token_usage:
                self.on_token_usage(prompt_tokens, completion_tokens, 0)
                
        return self._normalize_openai_response(response)

    # =========================================================================
    # GIỮ LẠI HÀM call_llm CŨ ĐỂ TƯƠNG THÍCH NGƯỢC (Evaluator, Lessons...)
    # =========================================================================

    def call_llm(self, prompt: str, system_instruction: str, build_cache_func, stop_sequences: list[str] = None) -> str:
        """
        [LEGACY] Gọi LLM trả về chuỗi văn bản thuần (không dùng function calling).
        Vẫn được sử dụng bởi Evaluator (run_gan_evaluation) và Lessons Distillation
        vì chúng không cần tool calling.
        """
        if self.mock_mode:
            return self.get_mock_agent_response(self.current_role)
            
        max_retries = 3
        backoff = 2
        
        for attempt in range(max_retries):
            try:
                if self.provider == "gemini":
                    from google.genai import types
                    
                    active_model = self.model_name or "gemini-2.5-flash"
                    
                    cache_key = f"shared_cache_{active_model}"
                    cache_name = self.gemini_caches.get(cache_key)
                    
                    if not cache_name:
                        try:
                            print(f"⚡ [Harness Gemini Cache]: Đang tạo Context Cache dùng chung cho model {active_model}...")
                            model_id = active_model
                            if not model_id.startswith("models/"):
                                model_id = f"models/{model_id}"
                                
                            cache_contents = build_cache_func()
                            
                            cache = self.client.caches.create(
                                model=model_id,
                                config=types.CreateCachedContentConfig(
                                    contents=cache_contents,
                                    ttl="3600s"
                                )
                            )
                            cache_name = cache.name
                            self.gemini_caches[cache_key] = cache_name
                            print(f"✅ [Harness Gemini Cache]: Đã kích hoạt Cache dùng chung thành công ({cache_name})")
                        except Exception as e:
                            print(f"⚠️ [Harness Gemini Cache Warning]: Không tạo được Cache ({e}). Sẽ fallback về chế độ bình thường.")
                            cache_name = None
                    
                    model_id = active_model
                    if not model_id.startswith("models/"):
                        model_id = f"models/{model_id}"
        
                    if cache_name:
                        combined_prompt = f"[SYSTEM_INSTRUCTION]\n{system_instruction}\n[/SYSTEM_INSTRUCTION]\n\n{prompt}"
                        response = self.client.models.generate_content(
                            model=model_id,
                            contents=combined_prompt,
                            config=types.GenerateContentConfig(
                                cached_content=cache_name,
                                temperature=0.0,
                                stop_sequences=stop_sequences
                            )
                        )
                    else:
                        response = self.client.models.generate_content(
                            model=model_id,
                            contents=prompt,
                            config=types.GenerateContentConfig(
                                system_instruction=system_instruction,
                                temperature=0.0,
                                stop_sequences=stop_sequences
                            )
                        )
                    
                    usage = getattr(response, 'usage_metadata', None)
                    if usage:
                        prompt_tokens = getattr(usage, 'prompt_token_count', 0)
                        completion_tokens = getattr(usage, 'candidates_token_count', 0)
                        cached_tokens = getattr(usage, 'cached_content_token_count', 0)
                        total_tokens = getattr(usage, 'total_token_count', 0)
                        log_msg = (
                            f"\n📊 [GEMINI API USAGE]:\n"
                            f"   ├─ 📥 Input Tokens: {prompt_tokens} ({cached_tokens} cached)\n"
                            f"   ├─ 📤 Output Tokens: {completion_tokens}\n"
                            f"   └─ 🧮 Total Tokens: {total_tokens}\n"
                        )
                        print(log_msg)
                        with open(self.log_file, "a", encoding="utf-8") as f:
                            f.write(log_msg)
                        if self.on_token_usage:
                            self.on_token_usage(prompt_tokens, completion_tokens, cached_tokens)
                            
                    response_text = response.text if hasattr(response, 'text') else str(response)
                    if response_text is None:
                        raise ValueError("Gemini API returned empty/blocked response (response.text is None)")
                    return response_text
                elif self.provider == "claude":
                    kwargs = {
                        "model": self.model_name,
                        "max_tokens": 4000,
                        "temperature": 0.0,
                        "system": [
                            {
                                "type": "text",
                                "text": system_instruction,
                                "cache_control": {"type": "ephemeral"}
                            }
                        ],
                        "messages": [
                            {
                                "role": "user",
                                "content": [
                                    {
                                        "type": "text",
                                        "text": prompt,
                                        "cache_control": {"type": "ephemeral"}
                                    }
                                ]
                            }
                        ],
                        "extra_headers": {
                            "anthropic-beta": "prompt-caching-2024-07-31"
                        }
                    }
                    if stop_sequences:
                        kwargs["stop_sequences"] = stop_sequences
                    response = self.client.messages.create(**kwargs)

                    usage = getattr(response, 'usage', None)
                    if usage:
                        input_tokens = getattr(usage, 'input_tokens', 0)
                        output_tokens = getattr(usage, 'output_tokens', 0)
                        cache_read = getattr(usage, 'cache_read_input_tokens', 0) or 0
                        cache_creation = getattr(usage, 'cache_creation_input_tokens', 0) or 0
                        log_msg = (
                            f"\n📊 [CLAUDE API USAGE]:\n"
                            f"   ├─ 📥 Input Tokens: {input_tokens}\n"
                            f"   ├─ 📤 Output Tokens: {output_tokens}\n"
                            f"   ├─ ♻️  Cache Read (hit): {cache_read} tokens\n"
                            f"   └─ 🆕 Cache Creation (miss): {cache_creation} tokens\n"
                        )
                        print(log_msg)
                        with open(self.log_file, "a", encoding="utf-8") as f:
                            f.write(log_msg)
                        if self.on_token_usage:
                            self.on_token_usage(input_tokens, output_tokens, cache_read)

                    return response.content[0].text
                else:
                    # OpenAI và DeepSeek
                    kwargs = {
                        "model": self.model_name,
                        "messages": [
                            {"role": "system", "content": system_instruction},
                            {"role": "user", "content": prompt}
                        ],
                        "temperature": 0.0
                    }
                    if stop_sequences:
                        kwargs["stop"] = stop_sequences
                    response = self.client.chat.completions.create(**kwargs)
                    
                    usage = getattr(response, 'usage', None)
                    if usage:
                        prompt_tokens = getattr(usage, 'prompt_tokens', 0)
                        completion_tokens = getattr(usage, 'completion_tokens', 0)
                        log_msg = (
                            f"\n📊 [OPENAI/DEEPSEEK API USAGE]:\n"
                            f"   ├─ 📥 Input Tokens: {prompt_tokens}\n"
                            f"   └─ 📤 Output Tokens: {completion_tokens}\n"
                        )
                        print(log_msg)
                        with open(self.log_file, "a", encoding="utf-8") as f:
                            f.write(log_msg)
                        if self.on_token_usage:
                            self.on_token_usage(prompt_tokens, completion_tokens, 0)
                            
                    return response.choices[0].message.content
            except Exception as e:
                err_msg = str(e)
                is_rate_limit = any(keyword in err_msg or keyword in err_msg.lower() for keyword in ["429", "resource_exhausted", "quota", "rate limit", "rate_limit"])
                
                if attempt < max_retries - 1:
                    sleep_time = 15 * (attempt + 1) if is_rate_limit else backoff
                    print(f"⚠️ [Harness API Warning]: Lần gọi {attempt + 1} cho {self.provider.upper()} thất bại ({err_msg}). Đang thử lại sau {sleep_time} giây...")
                    time.sleep(sleep_time)
                    if not is_rate_limit:
                        backoff *= 2
                else:
                    print(f"\n🤖 [Hệ thống]: Tự động chuyển đổi sang chế độ giả lập (Mock Mode) để tiếp tục quy trình...")
                    print(f"=============================================================\n")
                    self.mock_mode = True
                    return self.get_mock_agent_response(self.current_role)
    def _get_mock_tool_response(self, role: str) -> dict:
        """
        Trả về phản hồi mock để duy trì luồng pipeline (không crash).
        Agent sẽ tự động gọi finish_task để pipeline chạy qua các phase.
        """
        print(f"\n⚡ [Harness Mock]: Lớp '{role}' chạy ở chế độ giả lập.")
        return {
            "thought": f"Mock {role}: hoàn thành nhiệm vụ.",
            "tool_calls": [{
                "id": f"mock_{int(time.time())}",
                "function": {
                    "name": "finish_task",
                    "arguments": {"summary": f"Mock {role} completed task."}
                }
            }]
        }

    def get_mock_agent_response(self, role: str) -> str:
        """[LEGACY] Trả về phản hồi giả lập dạng text cho hàm call_llm cũ."""
        if role == "Planner":
            plan_md = (
                "### AI EXECUTION PLAN: TÍCH HỢP THỰC THỂ POSTCATEGORY\n\n"
                "1. **Phân tích Kiến trúc**:\n"
                "   - Lớp Domain: Tạo thực thể `PostCategory.cs` kế thừa từ `BaseEntity`.\n"
                "   - Lớp Application: Tạo Command/Query để thêm mới và truy vấn danh mục bài viết thông qua MediatR.\n"
                "2. **Kịch bản kiểm thử (Test Cases)**:\n"
                "   - Viết Integration Test kiểm chứng việc tạo mới PostCategory thành công với dữ liệu hợp lệ."
            )
            return f"THOUGHT: Tôi đã lập xong kế hoạch chi tiết.\nACTION: finish_task('{plan_md}')\n"
        elif role == "TestWriter":
            return "THOUGHT: Tôi đã viết xong các file test cần thiết.\nACTION: finish_task('Đã viết thành công integration tests cho PostCategory.')\n"
        elif role == "Developer":
            return "THOUGHT: Các bài test đã pass thành công! Tôi sẽ gọi finish_task.\nACTION: finish_task('Đã hiện thực hóa PostCategory.cs và vượt qua tất cả các bài kiểm thử.')\n"
        return "THOUGHT: Kết thúc.\nACTION: finish_task('Done')\n"
