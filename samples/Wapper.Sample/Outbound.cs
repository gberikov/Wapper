using Wapper.Messages;

namespace Wapper.Sample;

/// <summary>What a send returns to the caller, and how its failures are told apart.</summary>
internal static class Outbound
{
    /// <summary>
    /// Runs a send and turns the outcome into an HTTP answer.
    /// </summary>
    /// <remarks>
    /// By the time an exception reaches here the retryable rejections — throughput, the pair
    /// limit, transient server errors — have already been retried. What is left is worth
    /// branching on, and the branch is always on <see cref="WhatsAppError.Code"/>: Meta
    /// documents the HTTP status as unstable.
    /// </remarks>
    public static async Task<IResult> SendAsync(Func<Task<SentMessage>> send)
    {
        try
        {
            var sent = await send();

            // Accepted, not delivered. Delivery arrives later on the webhook as a
            // MessageStatusChanged carrying this id.
            return Results.Accepted(value: new { sent.Id, sent.RecipientId });
        }
        catch (WhatsAppApiException exception) when (exception.Code == WhatsAppErrorCodes.ReEngagementRequired)
        {
            // The 24-hour customer service window has closed. Only a template gets through.
            return Results.Conflict(new { error = "Outside the customer service window; send a template instead." });
        }
        catch (WhatsAppApiException exception) when (exception.Code == WhatsAppErrorCodes.UserOptedOut)
        {
            return Results.Conflict(new { error = "The customer has opted out of marketing messages." });
        }
        catch (WhatsAppApiException exception)
        {
            // Everything else Meta said no to. TraceId is what to quote in a ticket.
            return Results.BadRequest(new { exception.Code, exception.Error.Message, exception.Error.TraceId });
        }
        catch (WhatsAppRateLimitedException exception)
        {
            // A budget is exhausted and the retries are spent. RetryAfter is an estimate.
            return Results.Json(
                new { retryAfterSeconds = exception.RetryAfter.TotalSeconds },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (ArgumentException exception)
        {
            // Caught before the call: a fourth button, an oversized file, a malformed id.
            return Results.BadRequest(new { error = exception.Message });
        }
    }
}
