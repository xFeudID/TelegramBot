using System;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    private static ITelegramBotClient bot;
    private static SQLiteConnection dbConnection;

    static async Task Main(string[] args)
    {
        bot = new TelegramBotClient("7573861967:AAGWRB86fCSbOc4qP3tOaeSL6d68e68ezac");

        // Создаем базу данных SQLite
        CreateDatabase();

        using var cts = new CancellationTokenSource();

        // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
        };
        bot.StartReceiving(
            HandleUpdateAsync,
            HandleErrorAsync,
            receiverOptions,
            cancellationToken: cts.Token
        );

        var me = await bot.GetMeAsync();
        Console.WriteLine($"Start listening for @{me.Username}");
        Console.ReadLine();

        // Send cancellation request to stop bot
        cts.Cancel();
    }

    private static void CreateDatabase()
    {
        if (!System.IO.File.Exists("users.db"))
        {
            SQLiteConnection.CreateFile("users.db");
        }

        dbConnection = new SQLiteConnection("Data Source=users.db;Version=3;");
        dbConnection.Open();

        string sql = "CREATE TABLE IF NOT EXISTS Users (Id INTEGER PRIMARY KEY AUTOINCREMENT, TelegramId INTEGER, FullName TEXT, PhoneNumber TEXT)";
        using var cmd = new SQLiteCommand(sql, dbConnection);
        cmd.ExecuteNonQuery();
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Type == UpdateType.Message && update.Message!.Type == MessageType.Contact)
        {
            var contact = update.Message.Contact;
            var userId = contact.UserId;
            var fullName = contact.FirstName + " " + contact.LastName;
            var phoneNumber = contact.PhoneNumber;

            // Проверяем, есть ли пользователь в базе данных
            if (!IsUserExists(userId.Value))
            {
                // Сохраняем данные в базе данных
                SaveUserToDatabase(userId.Value, fullName, phoneNumber);

                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: "Данные успешно сохранены! Вы успешно авторизованы.",
                    cancellationToken: cancellationToken
                );
            }
            else
            {
                await botClient.SendTextMessageAsync(
                    chatId: update.Message.Chat.Id,
                    text: "Вы уже авторизованы.",
                    cancellationToken: cancellationToken
                );
            }
        }
        else if (update.Type == UpdateType.Message && update.Message!.Text == "/start")
        {
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                KeyboardButton.WithRequestContact("Поделиться контактом")
            });

            await botClient.SendTextMessageAsync(
                chatId: update.Message.Chat.Id,
                text: "Пожалуйста, поделитесь своим номером телефона для авторизации.",
                replyMarkup: keyboard,
                cancellationToken: cancellationToken
            );
        }
        else if (update.Type == UpdateType.Message && update.Message!.Text == "/viewdb")
        {
            var users = GetUsersFromDatabase();
            var message = "Содержимое базы данных:\n";
            foreach (var user in users)
            {
                message += $"ID: {user.Item1}, Telegram ID: {user.Item2}, ФИО: {user.Item3}, Телефон: {user.Item4}\n";
            }

            await botClient.SendTextMessageAsync(
                chatId: update.Message.Chat.Id,
                text: message,
                cancellationToken: cancellationToken
            );
        }
        else if (update.Type == UpdateType.Message && update.Message!.Text == "/cleardb")
        {
            ClearDatabase();
            await botClient.SendTextMessageAsync(
                chatId: update.Message.Chat.Id,
                text: "База данных очищена.",
                cancellationToken: cancellationToken
            );
        }
    }

    private static bool IsUserExists(long telegramId)
    {
        string sql = "SELECT COUNT(*) FROM Users WHERE TelegramId = @TelegramId";
        using var cmd = new SQLiteCommand(sql, dbConnection);
        cmd.Parameters.AddWithValue("@TelegramId", telegramId);
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        return count > 0;
    }

    private static void SaveUserToDatabase(long telegramId, string fullName, string phoneNumber)
    {
        string sql = "INSERT INTO Users (TelegramId, FullName, PhoneNumber) VALUES (@TelegramId, @FullName, @PhoneNumber)";
        using var cmd = new SQLiteCommand(sql, dbConnection);
        cmd.Parameters.AddWithValue("@TelegramId", telegramId);
        cmd.Parameters.AddWithValue("@FullName", fullName);
        cmd.Parameters.AddWithValue("@PhoneNumber", phoneNumber);
        cmd.ExecuteNonQuery();
    }

    private static Tuple<int, long, string, string>[] GetUsersFromDatabase()
    {
        string sql = "SELECT * FROM Users";
        using var cmd = new SQLiteCommand(sql, dbConnection);
        using var reader = cmd.ExecuteReader();

        var users = new System.Collections.Generic.List<Tuple<int, long, string, string>>();
        while (reader.Read())
        {
            users.Add(Tuple.Create(
                reader.GetInt32(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3)
            ));
        }

        return users.ToArray();
    }

    private static void ClearDatabase()
    {
        string sql = "DELETE FROM Users";
        using var cmd = new SQLiteCommand(sql, dbConnection);
        cmd.ExecuteNonQuery();
    }

    private static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var ErrorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(ErrorMessage);
        return Task.CompletedTask;
    }
}