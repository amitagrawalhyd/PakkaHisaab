namespace PakkaHisaab.Shared.Dtos;

public record RegisterRequest(string Email, string Password, string DisplayName, string? PhoneNumber);
public record LoginRequest(string Email, string Password);
public record AuthResponse(Guid UserId, string DisplayName, string AccessToken, DateTime ExpiresAtUtc, string RefreshToken);
public record RefreshRequest(string RefreshToken);
public record DeleteAccountRequest(string Password, string Confirmation); // Confirmation must equal "DELETE"
