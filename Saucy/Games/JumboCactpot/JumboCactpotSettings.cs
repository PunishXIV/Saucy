using System;

namespace Saucy.JumboCactpot;

public enum JumboCactpotNumberMode
{
    Random,
    Specific,
}

[Serializable]
public class JumboCactpotSettings
{
    public JumboCactpotNumberMode NumberMode { get; set; } = JumboCactpotNumberMode.Random;

    public string Ticket1Number { get; set; } = string.Empty;

    public string Ticket2Number { get; set; } = string.Empty;

    public string Ticket3Number { get; set; } = string.Empty;

    public string GetTicketNumber(int ticketIndex) =>
        ticketIndex switch
        {
            0 => Ticket1Number,
            1 => Ticket2Number,
            2 => Ticket3Number,
            _ => string.Empty,
        };

    public static bool TryParseTicketNumber(string? text, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (text.Length != 4)
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return int.TryParse(text, out number) && number is >= 0 and <= 9999;
    }

    public static void FormatTicketNumber(int number, Span<char> dest)
    {
        number = Math.Clamp(number, 0, 9999);
        dest[0] = (char)('0' + number / 1000 % 10);
        dest[1] = (char)('0' + number / 100 % 10);
        dest[2] = (char)('0' + number / 10 % 10);
        dest[3] = (char)('0' + number % 10);
    }
}
