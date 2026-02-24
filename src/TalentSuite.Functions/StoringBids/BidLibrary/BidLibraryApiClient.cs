using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using TalentSuite.Shared.Bids;

namespace TalentSuite.Functions.StoringBids.BidLibrary;

public sealed class BidLibraryApiClient(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : IBidLibraryApiClient
{
    public async Task<BidResponse?> GetBidAsync(string bidId, CancellationToken ct = default)
    {
        using var http = CreateHttpClient();

        var response = await http.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to fetch bid {bidId} from API. Status: {(int)response.StatusCode} {response.ReasonPhrase}");

        return await response.Content.ReadFromJsonAsync<BidResponse>(cancellationToken: ct);
    }

    public async Task<string> GetFinalAnswerTextAsync(string bidId, string questionId, CancellationToken ct = default)
    {
        using var http = CreateHttpClient();

        var response = await http.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/final-answer",
            ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return string.Empty;

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Failed to fetch final answer for question {questionId}. Status: {(int)response.StatusCode} {response.ReasonPhrase}");

        var finalAnswer = await response.Content.ReadFromJsonAsync<FinalAnswerResponse>(cancellationToken: ct);
        return finalAnswer?.AnswerText ?? string.Empty;
    }

    private HttpClient CreateHttpClient()
    {
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(GetRequiredApiBaseUrl(), UriKind.Absolute);

        var bearerToken = configuration["BidLibrary:ApiBearerToken"];
        if (!string.IsNullOrWhiteSpace(bearerToken))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        return http;
    }

    private string GetRequiredApiBaseUrl()
    {
        var fromSettings = configuration["BidLibrary:ApiBaseUrl"];
        if (!string.IsNullOrWhiteSpace(fromSettings))
            return fromSettings;

        var fromAspireServiceRef = configuration["services:talentserver:http:0"];
        if (!string.IsNullOrWhiteSpace(fromAspireServiceRef))
            return fromAspireServiceRef;

        throw new InvalidOperationException(
            "BidLibrary API base URL is not configured. Set 'BidLibrary:ApiBaseUrl' or provide a reference to 'talentserver'.");
    }
}
