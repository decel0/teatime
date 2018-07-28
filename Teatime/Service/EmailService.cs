﻿using System;

using MailKit.Net.Imap;
using MailKit;
using MimeKit;
using MailKit.Net.Smtp;
using Newtonsoft.Json;
using Teatime.Model;
using Teatime.Utils;

namespace Teatime.Service
{
    public class EmailService
    {
        private const string Host = "hmail.local";
        private const int ImapPort = 143;
        private const int SmtpPort = 25;

        public static void ListInboxFolders(Participant inboxOwner, ILogger logger)
        {
            using (var client = new ImapClient())
            {
                client.ServerCertificateValidationCallback = (s, c, h, e) => true; // Accept all certificates
                client.Connect(Host, ImapPort, useSsl: false);
                client.Authenticate(inboxOwner.EmailAddress, inboxOwner.EmailPassword);

                IMailFolder personal = client.GetFolder(client.PersonalNamespaces[0]);
                foreach (IMailFolder folder in personal.GetSubfolders(false))
                {
                    logger.Log($"[folder] {folder.Name}");
                }

                if ((client.Capabilities & (ImapCapabilities.SpecialUse | ImapCapabilities.XList)) != 0)
                {
                    IMailFolder drafts = client.GetFolder(SpecialFolder.Drafts);
                    logger.Log($"[special folder] {drafts.Name}");
                }
                else
                {
                    logger.Log("unable to get special folders");
                }

                client.Disconnect(true);
            }
        }

        public static void ListInboxMessages(Participant inboxOwner, ILogger logger)
        {
            using (var client = new ImapClient())
            {
                client.ServerCertificateValidationCallback = (s, c, h, e) => true; // Accept all certificates
                client.Connect(Host, ImapPort, useSsl: false);
                client.Authenticate(inboxOwner.EmailAddress, inboxOwner.EmailPassword);

                var inbox = client.Inbox; // Always available
                inbox.Open(FolderAccess.ReadOnly);

                logger.Log($"Total messages: {inbox.Count}");
                logger.Log($"Recent messages: {inbox.Recent}");

                for (int i = 0; i < inbox.Count; i++)
                {
                    var message = inbox.GetMessage(i);
                    logger.Log($"Subject: {message.Subject}");
                    string xml = message.TextBody;
                    TeatimeEmail tm = XmlConvert.DeserializeObject<TeatimeEmail>(xml);
                    logger.Log($"Type: {tm.Type}");
                    logger.Log($"Id: {tm.Id}");
                }

                foreach (var summary in inbox.Fetch(0, -1, MessageSummaryItems.Full | MessageSummaryItems.UniqueId))
                {
                    logger.Log($"[summary] {summary.Index:D2}: {summary.Envelope.Subject}");
                }

                client.Disconnect(true);
            }
        }

        public static void SendMessage(Participant sender, Participant recipient, ILogger logger)
        {
            using (var client = new SmtpClient())
            {
                client.Connect(Host, SmtpPort);
                client.Authenticate(sender.EmailAddress, sender.EmailPassword);

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(sender.Name, sender.EmailAddress));
                message.To.Add(new MailboxAddress(recipient.Name, recipient.EmailAddress));
                message.Subject = "Hello at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                TeatimeEmail m = new TeatimeEmail();
                m.Type = "TopicStarter";
                m.Id = Guid.NewGuid();

                //message.Body = new TextPart("plain") { Text = JsonConvert.SerializeObject(m) };
                message.Body = new TextPart("plain") { Text = XmlConvert.SerializeObject(m) };

                client.Send(message);
                logger.Log($"Message sent.");

                client.Disconnect(quit: true);
            }
        }
    }
}