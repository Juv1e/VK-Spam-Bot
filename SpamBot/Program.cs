using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using VkNet;
using VkNet.Enums;
using VkNet.Enums.Filters;
using VkNet.Enums.SafetyEnums;
using VkNet.Exception;
using VkNet.Model;
using VkNet.Model.Keyboard;
using VkNet.Model.RequestParams;

namespace SpamBot
{
    class Program
    {
        static Dictionary<long, string> state = new Dictionary<long, string>();
        static VkApi vkapi = new VkApi();
        static BotsLongPollHistoryResponse history;
        static string linkForPay = ""; // ссылка на оплату заказа, берется из файла settings.json
        static void Main(string[] args)
        {
            var strPath = AppDomain.CurrentDomain.BaseDirectory + "settings.json";
            if (!System.IO.File.Exists(strPath))
            {
                System.IO.File.Create(strPath).Dispose();
                File.WriteAllText(strPath, "{\n    \"access_token\":\"\",\n    \"group_id\":0,\n    \"LinkForPay\":\"\"\n}");
                Console.WriteLine("Заполните данные в файле settings.json");
            }
            else
            {
                JObject parse = JObject.Parse(File.ReadAllText(strPath));
                string token = parse["access_token"].ToString();
                ulong idGroup = Convert.ToUInt64(parse["group_id"]);
                linkForPay = parse["LinkForPay"].ToString();
                if (token == "")
                {
                    Console.WriteLine("Данные не заполнены.");
                    return;
                }
                vkapi.Authorize(new ApiAuthParams
                {
                    AccessToken = token
                });
                var server = vkapi.Groups.GetLongPollServer(idGroup);
                var ts = server.Ts;
                var key = server.Key;
                while (true)
                {
                    try
                    {
                        Biba:
                        try
                        {
                            history = vkapi.Groups.GetBotsLongPollHistory(new BotsLongPollHistoryParams
                            {
                                Key = key,
                                Server = server.Server,
                                Ts = ts,
                                Wait = 35
                            });
                            ts = history.Ts;
                        }
                        catch (VkNet.Exception.LongPollKeyExpiredException)
                        {
                            server = vkapi.Groups.GetLongPollServer(idGroup);
                            key = server.Key;

                        }
                        catch (VkNet.Exception.LongPollException)
                        {
                            server = vkapi.Groups.GetLongPollServer(idGroup);
                            ts = server.Ts;
                            key = server.Key;
                        }
                        catch (Exception)
                        {
                            goto Biba;
                        }
                        if (history?.Updates == null) continue;
                        foreach (var a in history.Updates)
                        {
                            new Thread(() => Govno(a)).Start();

                        }
                    }
                    catch (VkNet.Exception.LongPollException exception)
                    {
                        if (exception is LongPollOutdateException outdateException)
                            ts = outdateException.Ts;
                        else
                        {
                            server = vkapi.Groups.GetLongPollServer(idGroup);
                            ts = server.Ts;
                            key = server.Key;
                        }
                    }
                }
            }
                Console.ReadLine();
        }
        static void Govno(VkNet.Model.GroupUpdate.GroupUpdate a)
        {
            if (a.Type == GroupUpdateType.MessageNew)
            {
                string newMSG = a.MessageNew.Message.Text;
                var from_id = a.MessageNew.Message.FromId.Value;
                var chatID = a.MessageNew.Message.PeerId.Value;
                Console.WriteLine($"[id{from_id} | ChatID: {chatID}] - [{a.MessageNew.Message.Text}] // {a.MessageNew.Message.Payload}");
                foreach (KeyValuePair<long, string> keyValue in state)
                {
                    if (from_id == keyValue.Key)
                    {
                        StateCatcher(keyValue.Key, chatID, keyValue.Value, a.MessageNew.Message.Text, a);

                        return;
                    }
                }
                Command(a.MessageNew.Message.Text, a, chatID);
            }
        }
        static void StateCatcher(long id, long chatId, string current_state, string message, VkNet.Model.GroupUpdate.GroupUpdate netMessage)
        {
            if (current_state == "LINK_WAIT")
            {
                var spamId = Resolver(message) ?? 1488;
                if(spamId == 1488)
                {
                    SendMessage("🔬 Эта ссылка не является профилем.", id);
                    state.Remove(id);
                }
                else
                {
                    var getInfo = vkapi.Users.Get(new long[] { spamId }, ProfileFields.CanWritePrivateMessage).FirstOrDefault();
                    var first_name = getInfo.FirstName;
                    var last_name = getInfo.LastName;
                    var canwrite = getInfo.CanWritePrivateMessage;
                    if(!canwrite)
                    {
                        KeyboardBuilder keyclose = new KeyboardBuilder();
                        keyclose.AddButton("Выбрать жертву", "", KeyboardButtonColor.Primary);
                        MessageKeyboard keyboardclose = keyclose.Build();
                        SendMessageWithButton("У жертвы закрыты личные сообщения.", id, keyboardclose); // блять не ругайтесь пж я далбаеб у меня справка реально 
                        state.Remove(id);
                        return;
                    }
                    KeyboardBuilder key = new KeyboardBuilder();
                    key.AddButton("200 сообщений", "", KeyboardButtonColor.Primary);
                    key.AddLine();
                    key.AddButton("500 сообщений", "", KeyboardButtonColor.Primary);
                    key.AddLine();
                    key.AddButton("650 сообщений", "", KeyboardButtonColor.Primary);
                    key.AddLine();
                    key.AddButton("2000 сообщений", "", KeyboardButtonColor.Primary);
                    key.AddLine();
                    MessageKeyboard keyboard = key.Build();
                    SendMessageWithButton($"Выбрана жертва: [id{spamId}|{first_name} {last_name}]" +
                        $"\nКакое количество сообщений вы хотите отправить жертве?" +
                        $"\n\n1) 200 сообщений - 15 рублей" +
                        $"\n2) 500 сообщений - 25 рублей (рекомендуется)" +
                        $"\n3) 650 сообщений - 40 рублей" +
                        $"\n4) 2000 сообщений - 100 рублей (популярно)", id, keyboard);
                    state.Remove(id);
                    state.Add(id, "WAIT_TARIF");
                }     
            }
            else if (current_state == "WAIT_TARIF")
            {
                if (message.Contains("200") || message.Contains("500") || message.Contains("650") || message.Contains("2000"))
                {
                    KeyboardBuilder key = new KeyboardBuilder();
                    key.AddButton("Готово", "", KeyboardButtonColor.Primary);
                    key.AddLine();
                    key.AddButton("Отменить заказ", "", KeyboardButtonColor.Negative);
                    MessageKeyboard keyboard = key.Build();
                    SendMessageWithButton($"Выбрано: {message}\nДля оплаты перейдите по ссылке {linkForPay} \nПосле оплаты вернитесь в этот диалог, и напишите слово \"готово\" без ковычек", id, keyboard);
                    state.Remove(id);
                    state.Add(id, "WAIT_FOR_PAY");
                }
                else
                {
                    state.Remove(id);
                    KeyboardBuilder key = new KeyboardBuilder();
                    key.AddButton("Выбрать жертву", "", KeyboardButtonColor.Primary);
                    MessageKeyboard keyboard = key.Build();
                    SendMessageWithButton("Не выбран тариф.", id, keyboard);
                }
            }
            else if (current_state == "WAIT_FOR_PAY")
            {
                if (message == "Готово")
                {
                    SendMessage("Платеж еще не поступил.", id);
                }
                else if (message == "Отменить заказ")
                {
                    state.Remove(id);
                    KeyboardBuilder key = new KeyboardBuilder();
                    key.AddButton("Выбрать жертву", "", KeyboardButtonColor.Primary);
                    MessageKeyboard keyboard = key.Build();
                    SendMessageWithButton("Заказ отменён.", id, keyboard);
                }
            }
        }
        static void Command(string Message, VkNet.Model.GroupUpdate.GroupUpdate netMessage, long uid)
        {
            string msgs = Message;
            Message = Message.ToLower();
            if (Message == "start" || Message == "начать")
            {
                KeyboardBuilder key = new KeyboardBuilder();
                key.AddButton("Выбрать жертву", "", KeyboardButtonColor.Primary);
                MessageKeyboard keyboard = key.Build();
                SendMessageWithButton("Вас приветствует \"Бот - спамер\"! 💬\n\nЯ с легкостью могу отправить множество сообщений любому пользователю ВКонтакте.\n\nЖертва не узнает о том кто заказал спам. Полная анонимность и гарантия качественно выполненной работы! Чтобы начать, напишите \"Выбрать жертву\" или нажмите на кнопку", uid, keyboard);
            }
            else if (Message == "выбрать жертву")
            {
                KeyboardBuilder key = new KeyboardBuilder();
                MessageKeyboard keyboard = key.Build();
                SendMessageWithButton("Отправьте ссылку на жертву.", uid, keyboard);
                state.Add(uid, "LINK_WAIT");
            }
            else
            {
                KeyboardBuilder key = new KeyboardBuilder();
                key.AddButton("Выбрать жертву", "", KeyboardButtonColor.Primary);
                MessageKeyboard keyboard = key.Build();
                SendMessageWithButton("Вас приветствует \"Бот - спамер\"! 💬\n\nЯ с легкостью могу отправить множество сообщений любому пользователю ВКонтакте.\n\nЖертва не узнает о том кто заказал спам. Полная анонимность и гарантия качественно выполненной работы! Чтобы начать, напишите \"Выбрать жертву\" или нажмите на кнопку", uid, keyboard);
            }
        }
        static long? Resolver(string msg)
        {
            string pattern = @"(https?:\/\/)?(m\.?)?vk\.com\/";
            var value = System.Text.RegularExpressions.Regex.Match(msg, pattern);

            if (value.Success)
                msg = msg.Replace(value.Value, "");

            msg = msg.Replace("/", "");

            if (msg == "" || msg == null)
                return null;

            var resolved = vkapi.Utils.ResolveScreenName(msg);

            if (resolved != null && resolved.Type == VkObjectType.User)
                return resolved.Id;
            else
                return 1488;
        }
        static void SendMessage(string Body, long keks)
        {
            vkapi.Messages.Send(new MessagesSendParams
            {
                RandomId = 0,
                PeerId = keks,
                Message = Body
            });
        }
        static void SendMessageWithButton(string Body, long keks, MessageKeyboard keyboard)
        {
            vkapi.Messages.Send(new MessagesSendParams
            {
                RandomId = 0,
                PeerId = keks,
                Message = Body,
                Keyboard = keyboard
            });
        }
    }
}
