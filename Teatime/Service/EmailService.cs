﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using MailKit.Net.Imap;
using MailKit;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Search;
using Newtonsoft.Json;
using Teatime.Model;
using Teatime.Utils;

namespace Teatime.Service
{
    public class EmailService
    {
        public static void SendMessage(EmailAccount sender, SortedSet<Participant> recipients, string topicName, string messageText, ILogger logger)
        {
            using (var client = new SmtpClient())
            {
                client.Connect(sender.EmailHost, sender.SmtpPort);
                client.Authenticate(sender.EmailAddress, sender.EmailPassword);

                MimeMessage mimeMessage = new MimeMessage();
                mimeMessage.From.Add(new MailboxAddress(sender.Name, sender.EmailAddress));
                foreach (Participant r in recipients)
                {
                    mimeMessage.To.Add(new MailboxAddress(r.Name, r.EmailAddress));
                }
                mimeMessage.Subject = TeatimeEmail.SubjectTag + " " + DateTime.Now.ToString("o");

                TeatimeEmail te = new TeatimeEmail();
                te.FromEmailAddress = sender.EmailAddress;
                te.ToEmailAddresses = recipients.Select(r => r.EmailAddress).ToList();
                te.TopicName = topicName;
                te.MessageText = messageText;

                mimeMessage.Body = new TextPart("plain") { Text = JsonConvert.SerializeObject(te, Formatting.Indented) };

                client.Send(mimeMessage);
                logger.LogInfo($"E-Mail with topic \"{topicName}\" sent to \"{string.Join(", ", te.ToEmailAddresses)}\".");

                client.Disconnect(quit: true);
            }
        }

        public static List<Group> LoadData(EmailAccount inboxOwner, ILogger logger)
        {
            Dictionary<string, Group> groups = new Dictionary<string, Group>();

            using (var client = new ImapClient())
            {
                client.ServerCertificateValidationCallback = (s, c, h, e) => true; // Accept all certificates
                client.Connect(inboxOwner.EmailHost, inboxOwner.ImapPort, useSsl: false);
                client.Authenticate(inboxOwner.EmailAddress, inboxOwner.EmailPassword);

                var inbox = client.Inbox; // Always available
                inbox.Open(FolderAccess.ReadOnly);

                SearchQuery query = SearchQuery
                    .DeliveredAfter(DateTime.Parse("2018-07-28"))
                    .And(SearchQuery.SubjectContains(TeatimeEmail.SubjectTag));

                foreach (UniqueId messageId in inbox.Search(query))
                {
                    MimeMessage mimeMessage = inbox.GetMessage(messageId);
                    TeatimeEmail te = JsonConvert.DeserializeObject<TeatimeEmail>(mimeMessage.TextBody);
                    Participant sender = new Participant(te.FromEmailAddress);
                    Group g = AddOrGetGroup(groups, sender.EmailAddress, inboxOwner.EmailAddress, te.ToEmailAddresses);
                    Topic t = AddOrGetTopic(g, sender, te.TopicName);
                    AddMessage(t, sender, te.MessageText);
                    logger.LogInfo($"Loaded message from \"{te.FromEmailAddress}\" to \"{string.Join(", ", te.ToEmailAddresses)}\" for group \"{g.DisplayText}\" and topic \"{t.Name}\".");
                }

                client.Disconnect(true);
            }

            return groups.Values.ToList();
        }

        private static Group AddOrGetGroup(
            Dictionary<string, Group> groups, 
            string senderEmailAddress, 
            string inboxOwnerEmailAddress, 
            List<string> toEmailAddresses)
        {
            Group g = new Group();
            AddParticipantsToGroup(g, senderEmailAddress, inboxOwnerEmailAddress, toEmailAddresses);

            if (groups.ContainsKey(g.DisplayText))
            {
                g = groups[g.DisplayText];
            }
            else
            {
                groups.Add(g.DisplayText, g);
            }

            return g;
        }

        private static void AddParticipantsToGroup(
            Group g, 
            string senderEmailAddress, 
            string inboxOwnerEmailAddress,
            List<string> toEmailAddresses)
        {
            g.Participants.Add(new Participant(senderEmailAddress));

            foreach (string toEmailAddress in toEmailAddresses)
            {
                if (!toEmailAddress.Equals(inboxOwnerEmailAddress))
                {
                    g.Participants.Add(new Participant(toEmailAddress));
                }
            }
        }

        private static Topic AddOrGetTopic(Group g, Participant sender, string topicName)
        {
            Topic t = g.Topics.SingleOrDefault(i => i.Name.Equals(topicName));

            if (t == null)
            {
                t = new Topic();
                t.Name = topicName;
                t.Starter = sender;
                g.Topics.Add(t);
            }

            return t;
        }

        private static void AddMessage(Topic t, Participant sender, string messageText)
        {
            Message m = new Message();
            m.Sender = sender;
            m.Body = messageText;
            t.Messages.Add(m);
        }
    }
}