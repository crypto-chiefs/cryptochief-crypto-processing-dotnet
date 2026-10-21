using System.Net;
using System.Text;
using System.Text.Json;
using CryptoChief.Processing.Internal;
using CryptoChief.Processing.Models;
using FluentAssertions;
using Xunit;

namespace CryptoChief.Processing.Tests;

public class BatchPayoutsTests
{
    private const string EstimateBody = """
        {"total":5,"ok":1,"failed":4,"total_estimated_fee_fiat":"0.01","items":[
          {"index":0,"status":"ok","estimate":{"amount_requested":"1","amount_to_receive":"1"}},
          {"index":1,"status":"error","error":"FAILED_TO_ESTIMATE_FEE","http_status":400},
          {"index":2,"status":"error","error":"FAILED_TO_ESTIMATE_FEE","http_status":502},
          {"index":3,"status":"error","error":"FAILED_TO_ESTIMATE_FEE"},
          {"index":4,"status":"error","error":"FAILED_TO_ESTIMATE_FEE","http_status":599}
        ]}
        """;

    private const string ExecuteBody = """
        {"batch_uuid":"b-1","total":5,"accepted":1,"rejected":4,"items":[
          {"index":0,"order_id":"order-0","status":"queue","uuid":"p-1",
           "payout":{"uuid":"p-1","order_id":"order-0","status":"queue"}},
          {"index":1,"order_id":"order-1","status":"error","error":"FAILED_TO_ESTIMATE_FEE","http_status":400},
          {"index":2,"order_id":"order-2","status":"error","error":"FAILED_TO_ESTIMATE_FEE","http_status":502},
          {"index":3,"order_id":"order-3","status":"error","error":"FAILED_TO_ESTIMATE_FEE"},
          {"index":4,"order_id":"order-4","status":"error","error":"FAILED_TO_ESTIMATE_FEE","http_status":599}
        ]}
        """;

    [Theory]
    [InlineData("estimate", EstimateBody, "ok")]
    [InlineData("execute", ExecuteBody, "queue")]
    public async Task Batch_responses_keep_per_item_http_status(string operation, string body, string successStatus)
    {
        using var handler = new StubHandler(body);
        var client = NewClient(handler);
        var request = new BatchExecuteRequest
        {
            Items = Enumerable.Range(0, 5).Select(i => new ExecutePayoutRequest
            {
                OrderId = $"order-{i}",
                UserId = "user-1",
                Network = "ETH_MAINNET",
                Coin = "ETH",
                Amount = "1",
                ToAddress = "0xdest",
                UrlCallback = "https://shop.test/callback",
            }).ToArray(),
        };

        var response = operation == "estimate"
            ? await client.Payouts.BatchEstimateAsync(request)
            : await client.Payouts.BatchExecuteAsync(request);

        handler.Paths.Should().Equal($"/v1/payout/batch/{operation}");
        response.Items.Select(item => item.Index).Should().Equal(0, 1, 2, 3, 4);
        response.Items.Select(item => item.Status).Should().Equal(successStatus, "error", "error", "error", "error");
        response.Items.Select(item => item.HttpStatus).Should().Equal(null, 400, 502, null, 599);
        response.Items.Select(item => item.IsRetryable).Should().Equal(false, false, true, false, false);
        response.Items[0].Error.Should().BeNull();
        response.Items.Skip(1).Should().OnlyContain(item => item.Error == "FAILED_TO_ESTIMATE_FEE");

        if (operation == "execute")
        {
            response.Items.Select(item => item.OrderId).Should()
                .Equal("order-0", "order-1", "order-2", "order-3", "order-4");
            response.Items[0].Uuid.Should().Be("p-1");
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(200, false)]
    [InlineData(400, false)]
    [InlineData(408, false)]
    [InlineData(500, false)]
    [InlineData(502, true)]
    [InlineData(503, false)]
    [InlineData(504, false)]
    [InlineData(599, false)]
    public void IsRetryable_is_true_for_502_only(int? httpStatus, bool retryable)
    {
        var item = new BatchItemResult
        {
            Status = "error",
            Error = "FAILED_TO_ESTIMATE_FEE",
            HttpStatus = httpStatus,
        };

        item.IsRetryable.Should().Be(retryable);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(502)]
    [InlineData(599)]
    public void Http_status_serializes_and_round_trips_including_unknown_values(int httpStatus)
    {
        var item = new BatchItemResult
        {
            Status = "error",
            Error = "FAILED_TO_ESTIMATE_FEE",
            HttpStatus = httpStatus,
        };

        var json = JsonSerializer.Serialize(item, JsonDefaults.Options);

        json.ShouldBeJson($$"""
            {"index":0,"order_id":"","status":"error","error":"FAILED_TO_ESTIMATE_FEE","http_status":{{httpStatus}}}
            """);
        JsonSerializer.Deserialize<BatchItemResult>(json, JsonDefaults.Options)!
            .HttpStatus.Should().Be(httpStatus);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("queue")]
    [InlineData("error")]
    public void Absent_http_status_and_retry_helper_are_not_serialized(string status)
    {
        var json = JsonSerializer.Serialize(new BatchItemResult { Status = status }, JsonDefaults.Options);

        json.ShouldBeJson($$"""{"index":0,"order_id":"","status":"{{status}}"}""");
        JsonSerializer.Deserialize<BatchItemResult>(json, JsonDefaults.Options)!
            .HttpStatus.Should().BeNull();
    }

    private static CryptoChiefClient NewClient(HttpMessageHandler handler) =>
        new(new CryptoChiefClientOptions
        {
            MerchantId = "M-1",
            ApiKey = "K-1",
            BaseUrl = "https://test/",
            MaxRetries = 0,
        }, new HttpClient(handler), null);

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
