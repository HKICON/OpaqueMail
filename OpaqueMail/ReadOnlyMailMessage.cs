using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace OpaqueMail
{
    /// <summary>
    /// Represents an e-mail message that was received using the ImapClient or Pop3Client classes.
    /// Includes OpaqueMail extensions to facilitate handling of secure S/MIME messages.
    /// </summary>
    public class ReadOnlyMailMessage : OpaqueMail.MailMessage
    {
        #region Public Members
        /// <summary>Character set encoding of the message.</summary>
        public string CharSet = "";
        /// <summary>Language of message content.</summary>
        public string ContentLanguage = "";
        /// <summary>Content transfer encoding of the message.</summary>
        public string ContentTransferEncoding = "";
        /// <summary>Primary content type of the message.</summary>
        public string ContentType = "";
        /// <summary>Date sent.</summary>
        public DateTime Date;
        /// <summary>Delivered-To header.</summary>
        public string DeliveredTo = "";
        /// <summary>
        /// Extended e-mail headers.
        /// Only populated when the ReadOnlyMailMessage is instantiated with a parseExtendedHeaders setting of true.
        /// </summary>
        public ExtendedProperties ExtendedProperties;
        /// <summary>Flags representing the processed state of the message.</summary>
        public Flags Flags;
        /// <summary>Mailbox the message was read from.</summary>
        public string Mailbox;
        /// <summary>UID as specified by the IMAP server.</summary>
        public int ImapUid;
        /// <summary>Importance header.</summary>
        public string Importance = "";
        /// <summary>Index as specified by the IMAP or POP3 server.</summary>
        public int Index;
        /// <summary>In-Reply-To header.</summary>
        public string InReplyTo = "";
        /// <summary>Message ID header.</summary>
        public string MessageId;
        /// <summary>UIDL as specified by the POP3 server.</summary>
        public string Pop3Uidl;
        /// <summary>Flags determining whether specialized properties are returned with a ReadOnlyMailMessage.</summary>
        public ReadOnlyMailMessageProcessingFlags ProcessingFlags = ReadOnlyMailMessageProcessingFlags.IncludeRawHeaders | ReadOnlyMailMessageProcessingFlags.IncludeRawBody;
        /// <summary>String representation of the raw body received.</summary>
        public string RawBody;
        /// <summary>Raw flags returned with the message.</summary>
        public HashSet<string> RawFlags = new HashSet<string>();
        /// <summary>String representation of the raw headers received.</summary>
        public string RawHeaders;
        /// <summary>Array of values of Received and X-Received headers.</summary>
        public string[] ReceivedChain;
        /// <summary>Return-Path header.</summary>
        public string ReturnPath = "";
        /// <summary>X-Subject-Encryption header, as optionally used by OpaqueMail.</summary>
        public bool SubjectEncryption;
        #endregion Public Members

        #region Constructors
        /// <summary>
        /// Initializes a populated instance of the OpaqueMail.ReadOnlyMailMessage class representing the message text passed in.
        /// </summary>
        /// <param name="messageText">The raw contents of the e-mail message.</param>
        public ReadOnlyMailMessage(string messageText) : this(messageText, ReadOnlyMailMessageProcessingFlags.None, false) { }
        /// <summary>
        /// Initializes a populated instance of the OpaqueMail.ReadOnlyMailMessage class representing the message text passed in with attachments procesed according to the attachment filter flags.
        /// </summary>
        /// <param name="messageText">The raw contents of the e-mail message.</param>
        /// <param name="processingFlags">Flags determining whether specialized properties are returned with a ReadOnlyMailMessage.</param>
        public ReadOnlyMailMessage(string messageText, ReadOnlyMailMessageProcessingFlags processingFlags) : this(messageText, processingFlags, false) { }
        /// <summary>
        /// Initializes a populated instance of the OpaqueMail.ReadOnlyMailMessage class representing the message text passed in with attachments procesed according to the attachment filter flags.
        /// </summary>
        /// <param name="messageText">The raw contents of the e-mail message.</param>
        /// <param name="processingFlags">Flags determining whether specialized properties are returned with a ReadOnlyMailMessage.</param>
        /// <param name="parseExtendedHeaders">Whether to populate the ExtendedHeaders object.</param>
        public ReadOnlyMailMessage(string messageText, ReadOnlyMailMessageProcessingFlags processingFlags, bool parseExtendedHeaders)
        {
            // Remember which specialized attachments to include.
            ProcessingFlags = processingFlags;

            // Fix messages whose carriage returns have been stripped.
            if (messageText.IndexOf("\r") < 0)
                messageText = messageText.Replace("\n", "\r\n");

            // Separate the headers for processing.
            string headers;
            int cutoff = messageText.IndexOf("\r\n\r\n");
            if (cutoff > -1)
                headers = messageText.Substring(0, cutoff);
            else
                headers = messageText;

            // Set the raw headers property if requested.
            if ((processingFlags & ReadOnlyMailMessageProcessingFlags.IncludeRawHeaders) > 0)
                RawHeaders = headers;

            // Calculate the size of the message.
            Size = messageText.Length;

            // Temporary header variables to be processed by Functions.FromMailAddressString() later.
            string fromText = "";
            string toText = "";
            string ccText = "";
            string bccText = "";
            string replyToText = "";
            string subjectText = "";

            // Temporary header variables to be processed later.
            List<string> receivedChain = new List<string>();
            string receivedText = "";

            // Record keeping variable to handle headers that spawn multiple lines.
            string lastHeaderType = "";

            // Loop through each line of the headers.
            string[] headersList = headers.Replace("\r", "").Split('\n');
            foreach (string header in headersList)
            {
                bool headerProcessed = false;

                // Split header {name:value} pairs by the first colon found.
                int colonPos = header.IndexOf(":");
                if (colonPos > -1 && colonPos < header.Length - 1)
                {
                    headerProcessed = true;

                    string[] headerParts = new string[] { header.Substring(0, colonPos), header.Substring(colonPos + 2) };
                    string headerType = headerParts[0].ToLower();
                    string headerValue = headerParts[1];

                    // Set header variables for common headers.
                    switch (headerType)
                    {
                        case "cc":
                            ccText = headerValue;
                            break;
                        case "content-transfer-encoding":
                            ContentTransferEncoding = headerValue;
                            break;
                        case "content-language":
                            ContentLanguage = headerValue;
                            break;
                        case "content-type":
                            // If multiple content-types are passed, only process the first.
                            if (string.IsNullOrEmpty(ContentType))
                                ContentType = headerValue;
                            break;
                        case "date":
                            string dateString = headerValue;

                            // Ignore extraneous datetime information.
                            int dateStringParenthesis = dateString.IndexOf("(");
                            if (dateStringParenthesis > -1)
                                dateString = dateString.Substring(0, dateStringParenthesis - 1);

                            // Remove timezone suffix.
                            if (dateString.Substring(dateString.Length - 4, 1) == " ")
                                dateString = dateString.Substring(0, dateString.Length - 4);

                            DateTime.TryParse(dateString, out Date);
                            break;
                        case "delivered-to":
                            DeliveredTo = headerValue;
                            break;
                        case "from":
                            fromText = headerValue;
                            break;
                        case "importance":
                            Importance = headerValue;
                            break;
                        case "in-reply-to":
                            // Ignore opening and closing <> characters.
                            InReplyTo = headerValue;
                            if (InReplyTo.StartsWith("<"))
                                InReplyTo = InReplyTo.Substring(1);
                            if (InReplyTo.EndsWith(">"))
                                InReplyTo = InReplyTo.Substring(0, InReplyTo.Length - 1);
                            break;
                        case "message-id":
                            // Ignore opening and closing <> characters.
                            MessageId = headerValue;
                            if (MessageId.StartsWith("<"))
                                MessageId = MessageId.Substring(1);
                            if (MessageId.EndsWith(">"))
                                MessageId = MessageId.Substring(0, MessageId.Length - 1);
                            break;
                        case "received":
                        case "x-received":
                            if (!string.IsNullOrEmpty(receivedText))
                                receivedChain.Add(receivedText);

                            receivedText = headerValue;
                            break;
                        case "replyto":
                        case "reply-to":
                            replyToText = headerValue;
                            break;
                        case "return-path":
                            // Ignore opening and closing <> characters.
                            ReturnPath = headerValue;
                            if (ReturnPath.StartsWith("<"))
                                ReturnPath = ReturnPath.Substring(1);
                            if (ReturnPath.EndsWith(">"))
                                ReturnPath = ReturnPath.Substring(0, ReturnPath.Length - 1);
                            break;
                        case "sender":
                        case "x-sender":
                            if (headerValue.Length > 0)
                            {
                                MailAddressCollection senderCollection = Functions.FromMailAddressString(headerValue);
                                if (senderCollection.Count > 0)
                                    this.Sender = senderCollection[0];
                            }
                            break;
                        case "subject":
                            subjectText = headerValue;
                            break;
                        case "to":
                            toText = headerValue;
                            break;
                        case "x-priority":
                            switch (headerValue.ToUpper())
                            {
                                case "LOW":
                                    Priority = MailPriority.Low;
                                    break;
                                case "NORMAL":
                                    Priority = MailPriority.Normal;
                                    break;
                                case "HIGH":
                                    Priority = MailPriority.High;
                                    break;
                            }
                            break;
                        case "x-subject-encryption":
                            bool.TryParse(headerValue, out SubjectEncryption);
                            break;
                        default:
                            // Allow continuations if this header starts with whitespace.
                            headerProcessed = (!header.StartsWith("\t") && !header.StartsWith(" "));
                            break;
                    }

                    // Set header variables for advanced headers.
                    if (parseExtendedHeaders)
                    {
                        ExtendedProperties = new ExtendedProperties();

                        bool headerPreviouslyProcessed = headerProcessed;
                        switch (headerType)
                        {
                            case "acceptlanguage":
                            case "accept-language":
                                ExtendedProperties.AcceptLanguage = headerValue;
                                break;
                            case "authentication-results":
                                ExtendedProperties.AuthenticationResults = headerValue;
                                break;
                            case "bounces-to":
                            case "bounces_to":
                                ExtendedProperties.BouncesTo = headerValue;
                                break;
                            case "content-description":
                                ExtendedProperties.ContentDescription = headerValue;
                                break;
                            case "dispositionnotificationto":
                            case "disposition-notification-to":
                                ExtendedProperties.DispositionNotificationTo = headerValue;
                                break;
                            case "dkim-signature":
                            case "domainkey-signature":
                            case "x-google-dkim-signature":
                                ExtendedProperties.DomainKeySignature = headerValue;
                                break;
                            case "domainkey-status":
                                ExtendedProperties.DomainKeyStatus = headerValue;
                                break;
                            case "errors-to":
                                ExtendedProperties.ErrorsTo = headerValue;
                                break;
                            case "list-unsubscribe":
                            case "x-list-unsubscribe":
                                ExtendedProperties.ListUnsubscribe = headerValue;
                                break;
                            case "mailer":
                            case "x-mailer":
                                ExtendedProperties.Mailer = headerValue;
                                break;
                            case "organization":
                            case "x-originator-org":
                            case "x-originatororg":
                            case "x-organization":
                                ExtendedProperties.OriginatorOrg = headerValue;
                                break;
                            case "original-messageid":
                            case "x-original-messageid":
                                ExtendedProperties.OriginalMessageId = headerValue;
                                break;
                            case "originating-email":
                            case "x-originating-email":
                                ExtendedProperties.OriginatingEmail = headerValue;
                                break;
                            case "precedence":
                                ExtendedProperties.Precedence = headerValue;
                                break;
                            case "received-spf":
                                ExtendedProperties.ReceivedSPF = headerValue;
                                break;
                            case "references":
                                ExtendedProperties.References = headerValue;
                                break;
                            case "resent-date":
                                string dateString = headerValue;

                                // Ignore extraneous datetime information.
                                int dateStringParenthesis = dateString.IndexOf("(");
                                if (dateStringParenthesis > -1)
                                    dateString = dateString.Substring(0, dateStringParenthesis - 1);

                                // Remove timezone suffix.
                                if (dateString.Substring(dateString.Length - 4) == " ")
                                    dateString = dateString.Substring(0, dateString.Length - 4);

                                DateTime.TryParse(dateString, out ExtendedProperties.ResentDate);
                                break;
                            case "resent-from":
                                ExtendedProperties.ResentFrom = headerValue;
                                break;
                            case "resent-message-id":
                                ExtendedProperties.ResentMessageID = headerValue;
                                break;
                            case "thread-index":
                                ExtendedProperties.ThreadIndex = headerValue;
                                break;
                            case "thread-topic":
                                ExtendedProperties.ThreadTopic = headerValue;
                                break;
                            case "user-agent":
                            case "useragent":
                                ExtendedProperties.UserAgent = headerValue;
                                break;
                            case "x-auto-response-suppress":
                                ExtendedProperties.AutoResponseSuppress = headerValue;
                                break;
                            case "x-campaign":
                            case "x-campaign-id":
                            case "x-campaignid":
                            case "x-mllistcampaign":
                            case "x-rpcampaign":
                                ExtendedProperties.CampaignID = headerValue;
                                break;
                            case "x-delivery-context":
                                ExtendedProperties.DeliveryContext = headerValue;
                                break;
                            case "x-maillist-id":
                                ExtendedProperties.MailListId = headerValue;
                                break;
                            case "x-msmail-priority":
                                ExtendedProperties.MSMailPriority = headerValue;
                                break;
                            case "x-originalarrivaltime":
                            case "x-original-arrival-time":
                                dateString = headerValue;

                                // Ignore extraneous datetime information.
                                dateStringParenthesis = dateString.IndexOf("(");
                                if (dateStringParenthesis > -1)
                                    dateString = dateString.Substring(0, dateStringParenthesis - 1);

                                // Remove timezone suffix.
                                if (dateString.Substring(dateString.Length - 4) == " ")
                                    dateString = dateString.Substring(0, dateString.Length - 4);

                                DateTime.TryParse(dateString, out ExtendedProperties.OriginalArrivalTime);
                                break;
                            case "x-originating-ip":
                                ExtendedProperties.OriginatingIP = headerValue;
                                break;
                            case "x-rcpt-to":
                                if (headerValue.Length > 1)
                                    ExtendedProperties.RcptTo = headerValue.Substring(1, headerValue.Length - 2);
                                break;
                            case "x-csa-complaints":
                            case "x-complaints-to":
                            case "x-reportabuse":
                            case "x-report-abuse":
                            case "x-mail_abuse_inquiries":
                                ExtendedProperties.ReportAbuse = headerValue;
                                break;
                            case "x-spam-score":
                                ExtendedProperties.SpamScore = headerValue;
                                break;
                            default:
                                // Allow continuations if this header starts with whitespace.
                                if (!headerPreviouslyProcessed)
                                    headerProcessed = (!header.StartsWith("\t") && !header.StartsWith(" "));
                                break;
                        }
                    }

                    if (headerProcessed)
                        lastHeaderType = headerType;
                }

                // Handle continuations for headers spanning multiple lines.
                if (!headerProcessed)
                {
                    switch (lastHeaderType)
                    {
                        case "bcc":
                            bccText += header;
                            break;
                        case "cc":
                            ccText += header;
                            break;
                        case "content-type":
                            ContentType += header;
                            break;
                        case "delivered-to":
                            DeliveredTo += header;
                            break;
                        case "from":
                            fromText += header;
                            break;
                        case "message-id":
                            MessageId += header;
                            break;
                        case "received":
                        case "x-received":
                            receivedText += "\r\n" + header;
                            break;
                        case "reply-to":
                            replyToText += header;
                            break;
                        case "subject":
                            subjectText += header;
                            break;
                        case "to":
                            toText += header;
                            break;
                        default:
                            break;
                    }

                    // Set header variables for advanced headers.
                    if (parseExtendedHeaders)
                    {
                        switch (lastHeaderType)
                        {
                            case "authentication-results":
                                ExtendedProperties.AuthenticationResults += "\r\n" + header;
                                break;
                            case "dkim-signature":
                            case "domainkey-signature":
                            case "x-google-dkim-signature":
                                ExtendedProperties.DomainKeySignature += "\r\n" + header;
                                break;
                            case "list-unsubscribe":
                                ExtendedProperties.ListUnsubscribe += header;
                                break;
                            case "received-spf":
                                ExtendedProperties.ReceivedSPF += "\r\n" + header;
                                break;
                            case "references":
                                ExtendedProperties.References += "\r\n" + header;
                                break;
                            case "resent-from":
                                ExtendedProperties.ResentFrom += "\r\n" + header;
                                break;
                            case "thread-topic":
                                ExtendedProperties.ThreadTopic += header;
                                break;
                            case "x-report-abuse":
                                ExtendedProperties.ReportAbuse += header;
                                break;
                        }
                    }
                }
            }

            // Track all Received and X-Received headers.
            if (!string.IsNullOrEmpty(receivedText))
                receivedChain.Add(receivedText);
            ReceivedChain = receivedChain.ToArray();

            // Process the body if it's passed in.
            string body = "";
            if (cutoff > -1)
                 body = messageText.Substring(cutoff + 4);
            if (!string.IsNullOrEmpty(body))
            {
                // Set the raw body property if requested.
                if ((processingFlags & ReadOnlyMailMessageProcessingFlags.IncludeRawBody) > 0)
                    RawBody = body;

                // Parse body into MIME parts.
                List<MimePart> mimeParts = MimePart.ExtractMIMEParts(ContentType, ContentTransferEncoding, body, ProcessingFlags);

                // Process each MIME part.
                if (mimeParts.Count > 0)
                {
                    // Keep track of S/MIME signing and envelope encryption.
                    bool allMimePartsSigned = true, allMimePartsEncrypted = true, allMimePartsTripleWrapped = true;

                    // Process each MIME part.
                    for (int j = 0; j < mimeParts.Count; j++)
                    {
                        MimePart mimePart = mimeParts[j];

                        // If this MIME part isn't signed, the overall message isn't signed.
                        if (!mimePart.SmimeSigned)
                            allMimePartsSigned = false;

                        // If this MIME part isn't marked as being in an encrypted envelope, the overall message isn't encrypted.
                        if (!mimePart.SmimeEncryptedEnvelope)
                        {
                            // Ignore signatures and encryption blocks when determining if everything is encrypted.
                            if (!mimePart.ContentType.StartsWith("application/pkcs7-signature") && !mimePart.ContentType.StartsWith("application/x-pkcs7-signature") && !mimePart.ContentType.StartsWith("application/pkcs7-mime"))
                                allMimePartsEncrypted = false;
                        }

                        // If this MIME part isn't marked as being triple wrapped, the overall message isn't triple wrapped.
                        if (!mimePart.SmimeTripleWrapped)
                        {
                            // Ignore signatures and encryption blocks when determining if everything is triple wrapped.
                            if (!mimePart.ContentType.StartsWith("application/pkcs7-signature") && !mimePart.ContentType.StartsWith("application/x-pkcs7-signature") && !mimePart.ContentType.StartsWith("application/pkcs7-mime"))
                                allMimePartsTripleWrapped = false;
                        }

                        // Set the default primary body, defaulting to text/html and falling back to any text/*.
                        if (Body.Length < 1)
                        {
                            // If the MIME part is of type text/*, set it as the intial message body.
                            if (mimePart.ContentType.StartsWith("text/") || string.IsNullOrEmpty(mimePart.ContentType))
                            {
                                IsBodyHtml = mimePart.ContentType.StartsWith("text/html");
                                Body = mimePart.Body;
                                CharSet = mimePart.CharSet;
                                ContentType = mimePart.ContentType;
                            }
                            else
                            {
                                // If the MIME part isn't of type text/*, treat is as an attachment.
                                using (MemoryStream attachmentStream = new MemoryStream(mimePart.BodyBytes))
                                {
                                    Attachment attachment = new Attachment(attachmentStream, mimePart.Name, mimePart.ContentType);
                                    attachment.ContentId = mimePart.ContentID;
                                    Attachments.Add(attachment);
                                }
                            }
                        }
                        else
                        {
                            // If the current body isn't text/html and this is, replace the default body with the current MIME part.
                            if (!ContentType.StartsWith("text/html") && mimePart.ContentType.StartsWith("text/html"))
                            {
                                IsBodyHtml = true;
                                Body = mimePart.Body;
                                CharSet = mimePart.CharSet;
                                ContentType = mimePart.ContentType;
                            }
                            else
                            {
                                // If the MIME part isn't of type text/*, treat is as an attachment.
                                using (MemoryStream attachmentStream = new MemoryStream(mimePart.BodyBytes))
                                {
                                    Attachment attachment = new Attachment(attachmentStream, mimePart.Name, mimePart.ContentType);
                                    attachment.ContentId = mimePart.ContentID;
                                    Attachments.Add(attachment);
                                }
                            }
                        }
                    }

                    // OpaqueMail optional setting for protecting the subject.
                    if (SubjectEncryption && Body.StartsWith("Subject: "))
                    {
                        int linebreakPosition = Body.IndexOf("\r\n");
                        if (linebreakPosition > -1)
                        {
                            subjectText = Body.Substring(9, linebreakPosition - 9);
                            Body = Body.Substring(linebreakPosition + 2);
                        }
                    }

                    // Set the message's S/MIME attributes.
                    SmimeSigned = allMimePartsSigned;
                    SmimeEncryptedEnvelope = allMimePartsEncrypted;
                    SmimeTripleWrapped = allMimePartsTripleWrapped;
                }
                else
                {
                    // Process non-MIME messages.
                    Body = body;
                }
            }

            // Parse String representations of addresses into MailAddress objects.
            if (fromText.Length > 0)
            {
                MailAddressCollection fromAddresses = Functions.FromMailAddressString(fromText);
                if (fromAddresses.Count > 0)
                    From = fromAddresses[0];
            }

            if (toText.Length > 0)
            {
                To.Clear();
                MailAddressCollection toAddresses = Functions.FromMailAddressString(toText);
                foreach (MailAddress toAddress in toAddresses)
                    To.Add(toAddress);
            }

            if (ccText.Length > 0)
            {
                CC.Clear();
                MailAddressCollection ccAddresses = Functions.FromMailAddressString(ccText);
                foreach (MailAddress ccAddress in ccAddresses)
                    CC.Add(ccAddress);
            }

            if (bccText.Length > 0)
            {
                Bcc.Clear();
                MailAddressCollection bccAddresses = Functions.FromMailAddressString(bccText);
                foreach (MailAddress bccAddress in bccAddresses)
                    Bcc.Add(bccAddress);
            }

            if (replyToText.Length > 0)
            {
                ReplyToList.Clear();
                MailAddressCollection replyToAddresses = Functions.FromMailAddressString(replyToText);
                foreach (MailAddress replyToAddress in replyToAddresses)
                    ReplyToList.Add(replyToAddress);
            }

            // Decode international strings and remove escaped linebreaks.
            Subject = Functions.DecodeMailHeader(subjectText).Replace("\r", "").Replace("\n", "");
        }

        /// <summary>
        /// Initializes a populated instance of the OpaqueMail.MailMessage class representing the message text passed in.
        /// </summary>
        /// <param name="messageText">The raw headers of the e-mail message.</param>
        /// <param name="messageText">The raw body of the e-mail message.</param>
        public ReadOnlyMailMessage(string header, string body)
        {
            new ReadOnlyMailMessage(header + "\r\n" + body);
        }
        #endregion Constructors

        #region Public Methods
        /// <summary>
        /// Initializes a populated instance of the OpaqueMail.MailMessage class representing the message in the specified file.
        /// </summary>
        /// <param name="path">The file to open for reading.</param>
        public static ReadOnlyMailMessage LoadFile(string path)
        {
            if (File.Exists(path))
                return new ReadOnlyMailMessage(File.ReadAllText(path));
            else
                return null;
        }

        /// <summary>
        /// Process a list of flags as returned by the IMAP server.
        /// </summary>
        /// <param name="flagsString">List of space-separated flags.</param>
        public int ParseFlagsString(string flagsString)
        {
            int flagCounter = 0;

            string[] flags = flagsString.Split(' ');
            foreach (string flag in flags)
            {
                if (!RawFlags.Contains(flag))
                    RawFlags.Add(flag);

                flagCounter++;

                switch (flag.ToUpper())
                {
                    case "\\ANSWERED":
                        Flags = Flags | OpaqueMail.Flags.Answered;
                        break;
                    case "\\DELETED":
                        Flags = Flags | OpaqueMail.Flags.Deleted;
                        break;
                    case "\\DRAFT":
                        Flags = Flags | OpaqueMail.Flags.Draft;
                        break;
                    case "\\FLAGGED":
                        Flags = Flags | OpaqueMail.Flags.Flagged;
                        break;
                    case "\\RECENT":
                        Flags = Flags | OpaqueMail.Flags.Recent;
                        break;
                    case "\\SEEN":
                        Flags = Flags | OpaqueMail.Flags.Seen;
                        break;
                }
            }

            return flagCounter;
        }

        /// <summary>
        /// Saves a text representation of the message to the file specified.
        /// </summary>
        /// <param name="path">The file to save to.</param>
        public void SaveFile(string path)
        {
            File.WriteAllText(path, RawHeaders + "\r\n\r\n" + RawBody);
        }
        #endregion Public Methods
    }
}