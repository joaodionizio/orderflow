namespace OrderFlow.Application.Dtos.Responses;

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiraEm { get; set; }
}
