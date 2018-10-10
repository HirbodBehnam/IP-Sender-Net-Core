using System;
using System.IO;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Args;
using System.Text;

namespace IP_Sender
{
    class Program
    {
        static Config config;
        static TelegramBotClient Bot;
        static void Main(string[] args)
        {
            if(args.Length == 2 && args[0] == "--p")
            {
                Console.WriteLine(SHA256(args[1]));
                return;
            }
            //Configure mode
            if(args.Length == 1 && args[0] == "-c")
            {
                Config config = new Config();
                Console.Write("Enter bot token: ");
                config.Token = Console.ReadLine();
                Console.Write("Enter a name for this computer(DO NOT USE WHITE SPACE): ");
                config.PCName = Console.ReadLine();
                Console.Write("Enter a password for this computer: ");
                config.Password = SHA256(Console.ReadLine());
                Console.Write("Do you want to use \"Direct IP\"? (y/n): ");
                config.DirectIP = Console.ReadKey().Key == ConsoleKey.Y;
                Console.WriteLine();
                Console.Write("Log login failures? (y/n): ");
                config.LogFails = Console.ReadKey().Key == ConsoleKey.Y;
                Console.WriteLine();
                Console.Write("Log send IPs? (y/n): ");
                config.LogSends = Console.ReadKey().Key == ConsoleKey.Y;
                Console.WriteLine();
                Console.Write("Setup HTTP proxy? (y/n): ");
                if(Console.ReadKey().Key == ConsoleKey.Y)
                {
                    Console.WriteLine();
                    Config.Proxy p = new Config.Proxy();
                    Console.Write("Enter proxy IP: ");
                    p.IP = Console.ReadLine();
                    p.Port = -1;
                    do
                    {
                        Console.Write("Enter proxy port: ");
                        try
                        {
                            p.Port = Convert.ToInt32(Console.ReadLine());
                            if (p.Port < 0 || p.Port > 65535)
                                throw new FormatException("Port must be bigger than zero and less than 65536");
                        }
                        catch (FormatException)
                        {
                            Console.WriteLine("Please enter a valid port number.");
                        }
                    } while (p.Port < 0 || p.Port > 65535);
                    Console.Write("Setup username and password for this proxy? (y/n): ");
                    if (Console.ReadKey().Key == ConsoleKey.Y)
                    {
                        Console.WriteLine();
                        Console.Write("Enter proxy username: ");
                        p.User = Console.ReadLine();
                        Console.Write("Enter password: ");
                        p.Password = Console.ReadLine();
                    }
                    config.proxy = p;
                }
                Console.WriteLine();
                File.WriteAllText("bot_config.json", JsonConvert.SerializeObject(config));
                Console.WriteLine("\"bot_config.json\" saved.");
                return;
            }
            if (!File.Exists("bot_config.json"))
            {
                Console.WriteLine("\"bot_config.json\" does not exists.");
                Environment.Exit(1);
            }
            config = JsonConvert.DeserializeObject<Config>(File.ReadAllText("bot_config.json"));
            if(config.Token.Empty() || config.PCName.Empty() || config.Password.Empty())
            {
                Console.WriteLine("PC Name, Password or Token is empty.");
                Environment.Exit(1);
            }
            config.Password = config.Password.ToLower();
            //Now setup bot
            var httpClientHandler = new HttpClientHandler();
            if (config.DirectIP)
                httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => { return true; };
            if (config.proxy == null || (config.proxy.IP.Empty() && config.proxy.Port == 0))
            {
                var client = new HttpClient(httpClientHandler);
                Bot = new TelegramBotClient(config.Token, config.DirectIP,client);
            }else if(config.proxy.User.Empty() && config.proxy.User.Empty())
            {
                httpClientHandler.Proxy = new WebProxy(config.proxy.IP + ":" + config.proxy.Port);
                httpClientHandler.UseProxy = true;
                var client = new HttpClient(httpClientHandler);
                Bot = new TelegramBotClient(config.Token, config.DirectIP, client);
            }
            else
            {
                ICredentials credentials = new NetworkCredential(config.proxy.User, config.proxy.Password);
                httpClientHandler.Proxy = new WebProxy(config.proxy.IP + ":" + config.proxy.Port, true, null, credentials);
                httpClientHandler.UseProxy = true;
                var client = new HttpClient(httpClientHandler);
                Bot = new TelegramBotClient(config.Token, config.DirectIP, client);
            }
            var me = Bot.GetMeAsync().Result;
            Console.WriteLine($"[{DateTime.Now}]: Starting @{me.Username} bot. Press enter to stop bot.");
            Bot.OnMessage += BotOnMessageReceived;
            Bot.StartReceiving();
            Console.ReadLine();
            Bot.StopReceiving();
        }
        private async static void BotOnMessageReceived(object sender, MessageEventArgs e)
        {
            Telegram.Bot.Types.Message message = e.Message;
            if (message == null || message.Type != MessageType.Text) return;
            string[] msg = message.Text.Split(' ');
            if (msg.Length != 2) return;
            string ToSend = "null";
            if (msg[0] == config.PCName && SHA256(msg[1]) == config.Password)
            {
                await Bot.SendTextMessageAsync(message.Chat.Id, "Receiving IP...");
                try
                {
                    WebRequest request = WebRequest.Create("http://api.ipify.org/");
                    //We bypass any proxy if it's configured in system
                    request.Proxy = null;
                    WebResponse response = request.GetResponse();
                    Stream data = response.GetResponseStream();
                    using (StreamReader sr = new StreamReader(data))
                    {
                        ToSend = sr.ReadToEnd();
                    }
                    if (config.LogSends)
                    {
                        Console.WriteLine($"[{DateTime.Now}]: Send IP for @{message.From.Username}, UserID:{message.From.Id}, Name: {message.From.FirstName} {message.From.LastName}");
                    }
                }
                catch (Exception ex)
                {
                    ToSend = ex.ToString();
                    ConsoleColor color = Console.BackgroundColor;
                    Console.BackgroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[{DateTime.Now}]: Error Getting IP: {ToSend}");
                    Console.BackgroundColor = color;
                }
                await Bot.SendTextMessageAsync(message.Chat.Id, ToSend);
            }
            else if (msg[0] == config.PCName && config.LogFails)
            {
                ConsoleColor color = Console.BackgroundColor;
                Console.BackgroundColor = ConsoleColor.Red;
                Console.WriteLine($"[{DateTime.Now}]: Failed login attempt from @{message.From.Username}, UserID:{message.From.Id}, Name: {message.From.FirstName} {message.From.LastName}, Entered password: {msg[1]}  for {msg[0]} computer.");
                Console.BackgroundColor = color;
            }
        }
        /// <summary>
        /// Returns hashed string
        /// </summary>
        /// <param name="s">The string to hash</param>
        /// <returns></returns>
        /// https://stackoverflow.com/a/14709940/4213397
        public static string SHA256(string s)
        {
            var crypt = new System.Security.Cryptography.SHA256Managed();
            var hash = new StringBuilder();
            byte[] crypto = crypt.ComputeHash(Encoding.UTF8.GetBytes(s));
            foreach (byte theByte in crypto)
            {
                hash.Append(theByte.ToString("x2"));
            }
            return hash.ToString();
        }
    }
    static class StringExrension
    {
        /// <summary>
        /// Because I'm lazy
        /// </summary>
        /// <param name="str">The string to check</param>
        /// <returns></returns>
        public static bool Empty(this string str)
        {
            return string.IsNullOrWhiteSpace(str);
        }
    }
}
