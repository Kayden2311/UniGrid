using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using unigrid.Models.AI;

namespace unigrid.Services
{
    public class AIAssistantService : IAIAssistantService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AIAssistantService> _logger;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public AIAssistantService(HttpClient httpClient, ILogger<AIAssistantService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<unigrid.Models.AI.AssistantResponse> AskAsync(Guid userId, string message, List<unigrid.Models.AI.AssistantMessage>? history = null)
        {
            if (userId == Guid.Empty)
            {
                _logger.LogWarning("Assistant call attempted with empty user id.");
                return new unigrid.Models.AI.AssistantResponse { Reply = "Assistant unavailable (invalid user)." };
            }

            var payload = new AssistantRequest
            {
                Message = message,
                History = history ?? new List<AssistantMessage>()
            };

            var req = new HttpRequestMessage(HttpMethod.Post, "chat")
            {
                Content = JsonContent.Create(payload, options: _jsonOptions)
            };
            req.Headers.Add("X-User-Id", userId.ToString());

            _logger.LogInformation("Calling Chatbot service at {BaseAddress} with X-User-Id {UserId} (history length: {HistoryLen})",
                _httpClient.BaseAddress, userId, payload.History?.Count ?? 0);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)); // increased
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                resp.EnsureSuccessStatusCode();

                await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                var result = await JsonSerializer.DeserializeAsync<unigrid.Models.AI.AssistantResponse>(stream, _jsonOptions, cts.Token);

                sw.Stop();
                _logger.LogInformation("Chatbot responded in {Elapsed}s for user {UserId}", sw.Elapsed.TotalSeconds, userId);
                return result ?? new unigrid.Models.AI.AssistantResponse { Reply = "No response." };
            }
            catch (System.IO.IOException ioEx)
            {
                _logger.LogError(ioEx, "I/O error while calling chatbot for user {UserId} after {Elapsed}s", userId, sw.Elapsed.TotalSeconds);
                return new unigrid.Models.AI.AssistantResponse { Reply = "Assistant connection error. Try again later." };
            }
            catch (TaskCanceledException tex) when (!cts.IsCancellationRequested)
            {
                _logger.LogWarning(tex, "Chatbot request canceled (global timeout) for user {UserId} after {Elapsed}s", userId, sw.Elapsed.TotalSeconds);
                return new unigrid.Models.AI.AssistantResponse { Reply = "Assistant service timed out. Try again shortly." };
            }
            catch (TaskCanceledException tex)
            {
                _logger.LogWarning(tex, "Chatbot request canceled by token for user {UserId} after {Elapsed}s", userId, sw.Elapsed.TotalSeconds);
                return new unigrid.Models.AI.AssistantResponse { Reply = "Assistant request cancelled." };
            }
            catch (HttpRequestException hex)
            {
                _logger.LogError(hex, "Network error calling chatbot for user {UserId}", userId);
                return new unigrid.Models.AI.AssistantResponse { Reply = "Assistant network error." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Assistant call failed for user {UserId}", userId);
                return new unigrid.Models.AI.AssistantResponse { Reply = "Assistant service unavailable." };
            }
        }
    }
}
