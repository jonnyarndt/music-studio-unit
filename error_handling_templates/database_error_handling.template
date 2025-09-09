
// Database Operation Error Handling Template
var retryCount = 0;
const int maxRetries = 3;

while (retryCount < maxRetries)
{
    try
    {
        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using (var command = new SqlCommand(sql, connection))
            {
                return await command.ExecuteScalarAsync();
            }
        }
    }
    catch (SqlException ex) when (IsTransientError(ex.Number) && retryCount < maxRetries - 1)
    {
        retryCount++;
        var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
        _logger.LogWarning(ex, "Database operation failed, retrying in {Delay}s (attempt {Attempt}/{MaxRetries})", 
                          delay.TotalSeconds, retryCount, maxRetries);
        await Task.Delay(delay);
    }
    catch (SqlException ex)
    {
        _logger.LogError(ex, "Database operation failed after {RetryCount} attempts", retryCount);
        throw new DataAccessException("Database operation failed", ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error during database operation");
        throw;
    }
}

bool IsTransientError(int errorNumber)
{
    // Common transient SQL error numbers
    return errorNumber == 2 || errorNumber == 53 || errorNumber == 121 || errorNumber == 1205;
}
