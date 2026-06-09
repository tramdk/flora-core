"use strict";

/**
 * Self-Verification Dashboard - Client-Side Rendering.
 * Tự động fetch dữ liệu từ API JSON khi trang được tải,
 * và hỗ trợ nút re-run để kiểm thử lại theo thời gian thực.
 * 
 * Tất cả DOM manipulation sử dụng CSS class thay vì inline style để tuân thủ CSP.
 */

/** URL gốc của Verify API */
var VERIFY_API_URL = "/api/v1/verify";

/**
 * Lấy query string hiện tại (ví dụ: ?simulateFailure=true) để chuyển tiếp sang API.
 */
function getApiUrl() {
    var params = new URLSearchParams(window.location.search);
    var apiParams = new URLSearchParams();

    // Chuyển tiếp các query params hợp lệ sang API
    if (params.get("simulateFailure") === "true") {
        apiParams.set("simulateFailure", "true");
    }
    if (params.get("breakContract") === "true") {
        apiParams.set("breakContract", "true");
    }

    var queryString = apiParams.toString();
    return VERIFY_API_URL + (queryString ? "?" + queryString : "");
}

/**
 * Render toàn bộ dashboard từ dữ liệu JSON API response.
 */
function renderDashboard(report) {
    // 1. Cập nhật Thống kê
    document.getElementById("statTotal").innerText = report.totalComponents;
    document.getElementById("statTotal").setAttribute("data-verify-total", report.totalComponents);

    document.getElementById("statPassed").innerText = report.passed;
    document.getElementById("statPassed").setAttribute("data-verify-passed", report.passed);

    document.getElementById("statFailed").innerText = report.failed;
    document.getElementById("statFailed").setAttribute("data-verify-failed", report.failed);

    // 2. Cập nhật Banner chính
    var overallSuccess = report.failed === 0;
    var banner = document.getElementById("statusBanner");
    var container = document.querySelector(".container");

    container.setAttribute("data-verify-overall-status", overallSuccess ? "success" : "failed");

    if (overallSuccess) {
        banner.className = "banner status-success";
        banner.querySelector(".banner-icon").innerText = "✓";
        document.getElementById("statusBannerTitle").innerText = "Trạng thái: TẤT CẢ VƯỢT QUA";
        document.getElementById("statusBannerDesc").innerText = "Toàn bộ đặc tả tự xác thực của thành phần (Invariants & Contract) đã được giải phóng và kiểm tra hoàn tất.";
    } else {
        banner.className = "banner status-failed";
        banner.querySelector(".banner-icon").innerText = "✗";
        document.getElementById("statusBannerTitle").innerText = "Trạng thái: CÓ LỖI XẢY RA";
        document.getElementById("statusBannerDesc").innerText = "Phát hiện có thành phần tự kiểm thử bị thất bại. Xem chi tiết chẩn đoán của AI dưới đây.";
    }

    // 3. Render danh sách Component
    var compContainer = document.getElementById("componentList");
    compContainer.innerHTML = "";

    report.results.forEach(function (r, idx) {
        var card = document.createElement("div");
        card.className = "card component-card " + (r.success ? "pass" : "fail");
        card.setAttribute("data-verify-component-name", r.componentName);
        card.setAttribute("data-verify-component-status", r.success ? "pass" : "fail");
        card.style.setProperty("animation-delay", idx * 100 + "ms");

        var aiDiagnosticHtml = "";
        if (!r.success && r.aiDiagnosticHint) {
            aiDiagnosticHtml =
                '<div class="ai-diagnosis-banner" data-verify-ai-diagnostic="active">' +
                '<p class="ai-diagnosis-text">' + escapeHtml(r.aiDiagnosticHint) + "</p></div>";
        }

        var detailsHtml = "";
        if (r.details) {
            detailsHtml =
                '<details class="details-box" data-verify-error-details="active">' +
                "<summary>Chi tiết logs kỹ thuật</summary>" +
                "<pre><code>" + escapeHtml(r.details) + "</code></pre></details>";
        }

        var fixturesHtml = "";
        if (r.availableFixtures && r.availableFixtures.length > 0) {
            fixturesHtml = '<div class="fixtures-box" style="margin-top: 12px; font-size: 0.85rem; opacity: 0.85;">' +
                           '<strong>Fixtures:</strong> ' +
                           r.availableFixtures.map(function(f) {
                               var isActive = f === r.activeFixture;
                               var badgeStyle = isActive ? "background: rgba(14, 165, 233, 0.2); color: #0ea5e9; border: 1px solid #0ea5e9;" : "background: rgba(255,255,255,0.05); color: #ccc;";
                               return '<span style="display: inline-block; padding: 2px 8px; margin: 2px; border-radius: 4px; ' + badgeStyle + '">' +
                                      escapeHtml(f) + (isActive ? " (Active)" : "") +
                                      '</span>';
                           }).join(' ') +
                           '</div>';
        }

        var messageClass = "comp-message" + (r.success ? "" : " comp-message--fail");
        var isolateUrl = "/api/v1/verify/" + encodeURIComponent(r.componentName);

        card.innerHTML =
            '<div class="card-header">' +
                '<div class="comp-info">' +
                    '<span class="badge-indicator ' + (r.success ? "badge-pass" : "badge-fail") + '"></span>' +
                    '<h3 class="comp-name">' + escapeHtml(r.componentName) + "</h3>" +
                "</div>" +
                '<div style="display: flex; gap: 8px; align-items: center;">' +
                    '<a href="' + isolateUrl + '" target="_blank" style="font-size: 0.75rem; color: #38bdf8; text-decoration: none; border: 1px solid rgba(56, 189, 248, 0.3); padding: 2px 6px; border-radius: 4px;">Isolate ↗</a>' +
                    '<span class="badge-status ' + (r.success ? "status-badge-pass" : "status-badge-fail") + '">' +
                        (r.success ? "PASS" : "FAIL") +
                    '</span>' +
                '</div>' +
            "</div>" +
            '<div class="card-body">' +
                '<p class="' + messageClass + '">' + escapeHtml(r.message) + "</p>" +
                fixturesHtml +
                aiDiagnosticHtml +
                detailsHtml +
            "</div>";
        compContainer.appendChild(card);
    });
}

/**
 * Hiển thị trạng thái lỗi khi không thể kết nối API.
 */
function renderError(errorMessage) {
    var banner = document.getElementById("statusBanner");
    banner.className = "banner status-failed";
    banner.querySelector(".banner-icon").innerText = "✗";
    document.getElementById("statusBannerTitle").innerText = "LỖI HỆ THỐNG";
    document.getElementById("statusBannerDesc").innerText = "Không thể kết nối tới máy chủ tự kiểm thử: " + errorMessage;
}

/**
 * Chuyển UI sang trạng thái RUNNING (loading).
 */
function setRunningState() {
    var btn = document.getElementById("btnRunVerify");
    btn.classList.add("running");
    btn.querySelector(".btn-text").innerText = "Đang xác thực...";
    btn.querySelector(".btn-icon").innerText = "⏳";

    var banner = document.getElementById("statusBanner");
    banner.className = "banner status-running";
    banner.querySelector(".banner-icon").innerText = "⏳";
    document.getElementById("statusBannerTitle").innerText = "Đang tự xác thực...";
    document.getElementById("statusBannerDesc").innerText = "Đang chạy các đặc tả kiểm thử thời gian thực (real-time DOM & logic contracts)...";

    // Đưa tất cả các card hiện có về trạng thái chạy
    var cards = document.querySelectorAll(".component-card");
    cards.forEach(function (card) {
        card.className = "card component-card running";
        var indicator = card.querySelector(".badge-indicator");
        if (indicator) indicator.className = "badge-indicator badge-running";
        var badge = card.querySelector(".badge-status");
        if (badge) {
            badge.className = "badge-status status-badge-running";
            badge.innerText = "RUNNING";
        }
        var msg = card.querySelector(".comp-message");
        if (msg) {
            msg.classList.remove("comp-message--fail");
            msg.classList.add("comp-message--warning");
        }
        var diag = card.querySelector(".ai-diagnosis-banner");
        if (diag) diag.classList.add("hidden");
        var logs = card.querySelector(".details-box");
        if (logs) logs.classList.add("hidden");
    });
}

/**
 * Khôi phục nút bấm về trạng thái sẵn sàng.
 */
function resetButton() {
    var btn = document.getElementById("btnRunVerify");
    btn.classList.remove("running");
    btn.querySelector(".btn-text").innerText = "Khởi chạy tự kiểm thử";
    btn.querySelector(".btn-icon").innerText = "⚡";
}

/**
 * Fetch dữ liệu từ API và render dashboard.
 */
async function fetchAndRender() {
    setRunningState();

    try {
        // Trễ nhẹ để micro-animations mượt mà
        await new Promise(function (resolve) { setTimeout(resolve, 600); });

        var response = await fetch(getApiUrl());
        var data = await response.json();

        // Giải bọc nếu API trả về dạng ApiResponse wrapper
        var report = data.data ? data.data : data;

        renderDashboard(report);
    } catch (error) {
        console.error("Lỗi chạy thực thi verify:", error);
        renderError(error.message);
    } finally {
        resetButton();
    }
}

/**
 * Handler cho nút "Khởi chạy tự kiểm thử".
 */
function runLiveVerify() {
    var btn = document.getElementById("btnRunVerify");
    if (btn.classList.contains("running")) return;
    fetchAndRender();
}

/**
 * Escape HTML special characters to prevent XSS.
 */
function escapeHtml(text) {
    if (!text) return "";
    return text
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

// ========== Auto-Load: Fetch dữ liệu ngay khi trang sẵn sàng ==========
document.addEventListener("DOMContentLoaded", function () {
    // Gắn event listener cho nút (thay vì dùng onclick attribute)
    document.getElementById("btnRunVerify").addEventListener("click", runLiveVerify);

    // Tự động fetch & render lần đầu
    fetchAndRender();
});
