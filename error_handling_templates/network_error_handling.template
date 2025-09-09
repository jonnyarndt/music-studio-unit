
// Network Operation Error Handling Template
try
{
    using (var client = new HttpClient())
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
catch (HttpRequestException ex)
{
    _logger.LogError(ex, "HTTP request failed for URL: {Url}", url);
    // Implement retry logic or return cached data
    throw new ServiceUnavailableException("Network service temporarily unavailable", ex);
}
catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
{
    _logger.LogWarning(ex, "Request timeout for URL: {Url}", url);
    throw new TimeoutException("Request timed out", ex);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error during network operation for URL: {Url}", url);
    throw;
}
