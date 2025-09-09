
// File Operation Error Handling Template
try
{
    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
    using (var reader = new StreamReader(stream))
    {
        return await reader.ReadToEndAsync();
    }
}
catch (FileNotFoundException ex)
{
    _logger.LogWarning(ex, "File not found: {FilePath}", filePath);
    return GetDefaultContent(); // Provide default or create file
}
catch (UnauthorizedAccessException ex)
{
    _logger.LogError(ex, "Access denied to file: {FilePath}", filePath);
    throw new SecurityException("Insufficient permissions to access file", ex);
}
catch (IOException ex)
{
    _logger.LogError(ex, "I/O error reading file: {FilePath}", filePath);
    // Implement retry logic or alternative file source
    throw new DataAccessException("File operation failed", ex);
}
