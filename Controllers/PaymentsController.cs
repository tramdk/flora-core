using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Asp.Versioning;
using FloraCore.Application.Common.Models;
using FloraCore.Application.Common.Interfaces;
using FloraCore.Infrastructure.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Microsoft.Extensions.Options;

namespace FloraCore.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class PaymentsController(
    IIdempotentPaymentHandler paymentHandler, 
    IOptions<PaymentGatewaysOptions> paymentGatewaysOptions,
    IResourceManager resourceManager) : ControllerBase
{
    private readonly IIdempotentPaymentHandler _paymentHandler = paymentHandler ?? throw new ArgumentNullException(nameof(paymentHandler));
    private readonly PaymentGatewaysOptions _paymentGatewaysOptions = (paymentGatewaysOptions ?? throw new ArgumentNullException(nameof(paymentGatewaysOptions))).Value;
    private readonly IResourceManager _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));

    /// <summary>
    /// VNPay redirect callback (for Client/Browser redirection).
    /// </summary>
    [HttpGet("vnpay-callback")]
    [AllowAnonymous]
    public async Task<IActionResult> VnPayCallback()
    {
        var queryParams = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
        var txnRef = queryParams.GetValueOrDefault("vnp_TxnRef") ?? string.Empty;

        var dto = new PaymentCallbackDto { QueryParameters = queryParams };
        var success = await _paymentHandler.ProcessPaymentCallbackAsync("VNPAY", txnRef, dto);

        var frontendUrl = _paymentGatewaysOptions.FrontendUrl;
        if (string.IsNullOrEmpty(frontendUrl))
        {
            throw new InvalidOperationException(_resourceManager.GetString("FrontendUrlConfigMissing"));
        }

        if (success)
        {
            return Redirect($"{frontendUrl.TrimEnd('/')}/payment-success"); // Redirect to Frontend success page
        }
        return Redirect($"{frontendUrl.TrimEnd('/')}/payment-failed");
    }

    /// <summary>
    /// VNPay IPN (Instant Payment Notification) - called background by VNPay securely.
    /// </summary>
    [HttpGet("vnpay-ipn")]
    [AllowAnonymous]
    public async Task<IActionResult> VnPayIpn()
    {
        var queryParams = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
        var txnRef = queryParams.GetValueOrDefault("vnp_TxnRef") ?? string.Empty;

        var dto = new PaymentCallbackDto { QueryParameters = queryParams };
        var success = await _paymentHandler.ProcessPaymentCallbackAsync("VNPAY", txnRef, dto);

        if (success)
        {
            return Ok(new { RspCode = "00", Message = "Confirm Success" });
        }
        return Ok(new { RspCode = "97", Message = "Invalid Checksum" });
    }

    /// <summary>
    /// MoMo IPN (Instant Payment Notification) - called background by MoMo.
    /// </summary>
    [HttpPost("momo-ipn")]
    [AllowAnonymous]
    public async Task<IActionResult> MoMoIpn()
    {
        var queryParams = Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
        
        // MoMo can post via JSON body, let's also support reading body parameters
        if (Request.HasJsonContentType())
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                queryParams[prop.Name] = prop.Value.ToString();
            }
        }

        var orderId = queryParams.GetValueOrDefault("orderId") ?? string.Empty;
        var dto = new PaymentCallbackDto { QueryParameters = queryParams };
        var success = await _paymentHandler.ProcessPaymentCallbackAsync("MOMO", orderId, dto);

        if (success)
        {
            return NoContent();
        }
        return BadRequest();
    }

    /// <summary>
    /// PayOS Webhook - called background by PayOS when transactions are paid.
    /// </summary>
    [HttpPost("payos-webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> PayOsWebhook()
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync();

        using var doc = System.Text.Json.JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        
        if (!root.TryGetProperty("data", out var dataProp))
        {
            return BadRequest(new { Message = "Invalid payload" });
        }

        var orderCode = dataProp.GetProperty("orderCode").GetInt64().ToString();
        var dto = new PaymentCallbackDto { RawBody = rawBody };
        var success = await _paymentHandler.ProcessPaymentCallbackAsync("PAYOS", orderCode, dto);

        if (success)
        {
            return Ok(new { Status = "Success" });
        }
        return BadRequest();
    }
}
