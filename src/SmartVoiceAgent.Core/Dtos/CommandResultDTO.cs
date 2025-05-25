/// <summary>
/// Data Transfer Object representing the result of executing a command.
/// Contains status and an optional message.
/// </summary>
public record CommandResultDTO(
    bool Success,
    string? Message
);
