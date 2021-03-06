﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Threading.Tasks;

namespace OpaqueMail
{
    /// <summary>
    /// Represents a node in a MIME encoded message tree.
    /// </summary>
    public class MimePart
    {
        #region Public Members
        /// <summary>Return the string representation of the body.</summary>
        public string Body
        {
            get
            {
                return Encoding.UTF8.GetString(BodyBytes);
            }
        }
        /// <summary>Raw contents of the MIME part's body.</summary>
        public byte[] BodyBytes;
        /// <summary>Character Set used to encode the MIME part.</summary>
        public string CharSet = "";
        /// <summary>ID of the MIME part.</summary>
        public string ContentID = "";
        /// <summary>Content Type of the MIME part.</summary>
        public string ContentType = "";
        /// <summary>Filename of the MIME part.</summary>
        public string Name = "";
        /// <summary>Whether the MIME part is S/MIME signed.</summary>
        public bool SmimeSigned = false;
        /// <summary>Whether the MIME part is part of an S/MIME encrypted envelope.</summary>
        public bool SmimeEncryptedEnvelope = false;
        /// <summary>Whether the MIME part was S/MIME signed, had its envelope encrypted, and was then signed again.</summary>
        public bool SmimeTripleWrapped = false;
        #endregion Public Members

        #region Constructors
        /// <summary>
        /// Instantiate a MIME part based on the string representation of its body.
        /// </summary>
        /// <param name="name">Filename of the MIME part.</param>
        /// <param name="contentType">Content Type of the MIME part.</param>
        /// <param name="charset">Character Set used to encode the MIME part.</param>
        /// <param name="contentID">ID of the MIME part.</param>
        /// <param name="body">String representation of the MIME part's body.</param>
        public MimePart(string name, string contentType, string charset, string contentID, string body)
        {
            BodyBytes = Encoding.UTF8.GetBytes(body);
            ContentType = contentType.ToLower();
            ContentID = contentID;
            CharSet = charset;
            Name = name;
        }
        /// <summary>
        /// Instantiate a MIME part based on its body's byte array.
        /// </summary>
        /// <param name="name">Filename of the MIME part.</param>
        /// <param name="contentType">Content Type of the MIME part.</param>
        /// <param name="charset">Character Set used to encode the MIME part.</param>
        /// <param name="contentID">ID of the MIME part.</param>
        /// <param name="bodyBytes">The MIME part's raw bytes.</param>
        public MimePart(string name, string contentType, string charset, string contentID, byte[] bodyBytes)
        {
            BodyBytes = bodyBytes;
            ContentType = contentType;
            ContentID = contentID;
            CharSet = charset;
            Name = name;
        }
        #endregion Constructors

        #region Public Methods
        /// <summary>
        /// Extract a list of MIME parts from a multipart/* MIME encoded message.
        /// </summary>
        /// <param name="contentType">Content Type of the outermost MIME part.</param>
        /// <param name="contentTransferEncoding">Encoding of the outermost MIME part.</param>
        /// <param name="body">The outermost MIME part's contents.</param>
        /// <param name="processingFlags">Flags determining whether specialized properties are returned with a ReadOnlyMailMessage.</param>
        public static List<MimePart> ExtractMIMEParts(string contentType, string contentTransferEncoding, string body, ReadOnlyMailMessageProcessingFlags processingFlags)
        {
            List<MimePart> mimeParts = new List<MimePart>();

            if (contentType.StartsWith("multipart/"))
            {
                // Prepare to process each part of the multipart/* message.

                int cursor = 0;

                // Determine the outermost boundary name.
                string boundaryName = Functions.ReturnBetween(contentType, "boundary=\"", "\"");
                if (boundaryName.Length < 1)
                {
                    cursor = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
                    if (cursor > -1)
                        boundaryName = contentType.Substring(cursor + 9);
                    cursor = boundaryName.IndexOf(";");
                    if (cursor > -1)
                        boundaryName = boundaryName.Substring(0, cursor);
                }

                // Variables used for record keeping with signed S/MIME parts.
                int signatureBlock = -1;
                List<string> mimeBlocks = new List<string>();

                cursor = 0;
                while (cursor > -1)
                {
                    // Move cursor to the next boundary.
                    cursor = body.IndexOf("--" + boundaryName, cursor, StringComparison.OrdinalIgnoreCase);

                    if (cursor > -1)
                    {
                        // Calculate the end boundary of the current MIME part.
                        int boundaryEnd = body.IndexOf("--" + boundaryName, cursor + boundaryName.Length, StringComparison.OrdinalIgnoreCase);
                        if (boundaryEnd > -1)
                        {
                            string mimeContents = body.Substring(cursor + boundaryName.Length + 4, boundaryEnd - cursor - boundaryName.Length - 4);

                            // Extract the header portion of the current MIME part.
                            int mimeDivider = mimeContents.IndexOf("\r\n\r\n");
                            string mimeHeaders;
                            if (mimeDivider > -1)
                                mimeHeaders = mimeContents.Substring(0, mimeDivider);
                            else
                                mimeHeaders = mimeContents;

                            if (mimeHeaders.Length > 0)
                            {
                                // Extract the body portion of the current MIME part.
                                string mimeBody = mimeContents.Substring(mimeDivider + 4);
                                mimeBlocks.Add(mimeBody);

                                // Divide the MIME part's headers into its components.
                                string mimeCharSet = "", mimeContentDisposition = "", mimeContentID = "", mimeContentType = "", mimeContentTransferEncoding = "", mimeFileName = "";
                                ExtractMimeHeaders(mimeHeaders, out mimeContentType, out mimeCharSet, out mimeContentTransferEncoding, out mimeContentDisposition, out mimeFileName, out mimeContentID);

                                if (mimeContentType.StartsWith("multipart/"))
                                {
                                    // Recurse through embedded MIME parts.
                                    List<MimePart> returnedMIMEParts = ExtractMIMEParts(mimeContentType, mimeContentTransferEncoding, mimeBody, processingFlags);
                                    foreach (MimePart returnedMIMEPart in returnedMIMEParts)
                                        mimeParts.Add(returnedMIMEPart);
                                }
                                else
                                {
                                    // Keep track of whether this MIME part's body has already been processed.
                                    bool processed = false;

                                    if (mimeContentType.StartsWith("application/pkcs7-signature") || mimeContentType.StartsWith("application/x-pkcs7-signature"))
                                    {
                                        // Unless a flag has been set to include this *.p7s block, exclude it from attachments.
                                        if ((processingFlags & ReadOnlyMailMessageProcessingFlags.IncludeSmimeSignedData) == 0)
                                            processed = true;

                                        // Remember the signature block to use for later verification.
                                        signatureBlock = mimeBlocks.Count() - 1;
                                    }
                                    else if (mimeContentType.StartsWith("application/pkcs7-mime") || mimeContentType.StartsWith("application/x-pkcs7-mime"))
                                    {
                                        // Unless a flag has been set to include this *.p7m block, exclude it from attachments.
                                        processed = (processingFlags & ReadOnlyMailMessageProcessingFlags.IncludeSmimeEncryptedEnvelopeData) == 0;

                                        // Decrypt the MIME part and recurse through embedded MIME parts.
                                        List<MimePart> returnedMIMEParts = ReturnDecryptedMimeParts(mimeContentType, mimeContentTransferEncoding, mimeBody, processingFlags);
                                        if (returnedMIMEParts != null)
                                        {
                                            foreach (MimePart returnedMIMEPart in returnedMIMEParts)
                                                mimeParts.Add(returnedMIMEPart);
                                        }
                                    }
                                    else if (mimeContentType.StartsWith("application/ms-tnef") || mimeFileName.ToLower() == "winmail.dat")
                                    {
                                        // Process the TNEF encoded message.
                                        processed = true;
                                        TnefEncoding tnef = new TnefEncoding(Convert.FromBase64String(mimeBody));

                                        // If we were unable to extract content from this MIME, include it as an attachment.
                                        if ((tnef.Body.Length < 1 && tnef.MimeAttachments.Count < 1) || (processingFlags & ReadOnlyMailMessageProcessingFlags.IncludeWinMailData) > 0)
                                            processed = false;
                                        else
                                        {
                                            // Unless a flag has been set to include this winmail.dat block, exclude it from attachments.
                                            if ((processingFlags & ReadOnlyMailMessageProcessingFlags.IncludeWinMailData) > 0)
                                            {
                                                if (!string.IsNullOrEmpty(tnef.Body))
                                                    mimeParts.Add(new MimePart("winmail.dat", tnef.ContentType, "", "", Encoding.UTF8.GetBytes(tnef.Body)));
                                            }

                                            foreach (MimePart mimePart in tnef.MimeAttachments)
                                                mimeParts.Add(mimePart);
                                        }
                                    }

                                    if (!processed)
                                    {
                                        // Remove metadata from the Content Type string.
                                        int contentTypeSemicolonPos = mimeContentType.IndexOf(";");
                                        if (contentTypeSemicolonPos > -1)
                                            mimeContentType = mimeContentType.Substring(0, contentTypeSemicolonPos);

                                        // Decode and add the message to the MIME parts collection.
                                        switch (mimeContentTransferEncoding)
                                        {
                                            case "base64":
                                                mimeParts.Add(new MimePart(mimeFileName, mimeContentType, mimeCharSet, mimeContentID, Convert.FromBase64String(mimeBody)));
                                                break;
                                            case "quoted-printable":
                                                mimeParts.Add(new MimePart(mimeFileName, mimeContentType, mimeCharSet, mimeContentID, Functions.FromQuotedPrintable(mimeBody)));
                                                break;
                                            case "binary":
                                            case "7bit":
                                            case "8bit":
                                            default:
                                                mimeParts.Add(new MimePart(mimeFileName, mimeContentType, mimeCharSet, mimeContentID, mimeBody));
                                                break;
                                        }
                                    }
                                }
                                cursor += boundaryName.Length;
                            }
                            else
                                cursor = -1;
                        }
                        else
                            cursor = -1;
                    }
                }

                // If a PKCS signature was found and there's one other MIME part, verify the signature.
                if (signatureBlock > -1 && mimeBlocks.Count == 2)
                {
                    // Verify the signature.
                    if (VerifySignature(mimeBlocks[signatureBlock], mimeBlocks[1 - signatureBlock]))
                    {
                        // Stamp each MIME part found so far as signed, and if relevant, triple wrapped.
                        foreach (MimePart mimePart in mimeParts)
                        {
                            if (mimePart.SmimeSigned && mimePart.SmimeEncryptedEnvelope)
                                mimePart.SmimeTripleWrapped = true;

                            mimePart.SmimeSigned = true;
                        }
                    }
                }
            }
            else if (contentType.StartsWith("application/ms-tnef"))
            {
                // Process the TNEF encoded message.
                TnefEncoding tnef = new TnefEncoding(Convert.FromBase64String(body));

                // Unless a flag has been set to include this winmail.dat block, exclude it from attachments.
                if ((processingFlags & ReadOnlyMailMessageProcessingFlags.IncludeWinMailData) > 0)
                {
                    if (!string.IsNullOrEmpty(tnef.Body))
                        mimeParts.Add(new MimePart("winmail.dat", tnef.ContentType, "", "", Encoding.UTF8.GetBytes(tnef.Body)));
                }

                foreach (MimePart mimePart in tnef.MimeAttachments)
                    mimeParts.Add(mimePart);
            }
            else if (contentType.StartsWith("application/pkcs7-mime") || contentType.StartsWith("application/x-pkcs7-mime"))
            {
                // Unless a flag has been set to include this *.p7m block, exclude it from attachments.
                if ((processingFlags & ReadOnlyMailMessageProcessingFlags.IncludeSmimeEncryptedEnvelopeData) > 0)
                    mimeParts.Add(new MimePart("smime.p7m", contentType, "", "", body));

                // Decrypt the MIME part and recurse through embedded MIME parts.
                List<MimePart> returnedMIMEParts = ReturnDecryptedMimeParts(contentType, contentTransferEncoding, body, processingFlags);
                if (returnedMIMEParts != null)
                {
                    foreach (MimePart returnedMIMEPart in returnedMIMEParts)
                        mimeParts.Add(returnedMIMEPart);
                }
            }
            else
            {
                // Decode the message.
                switch (contentTransferEncoding)
                {
                    case "base64":
                        body = Functions.FromBase64(body);
                        break;
                    case "quoted-printable":
                        body = Functions.FromQuotedPrintable(body);
                        break;
                    case "binary":
                    case "7bit":
                    case "8bit":
                        break;
                }

                // Extract the headers from this MIME part.
                string mimeHeaders;
                int mimeDivider = body.IndexOf("\r\n\r\n");
                if (mimeDivider > -1 )
                    mimeHeaders = body.Substring(0, mimeDivider);
                else
                    mimeHeaders = body;

                // Divide the MIME part's headers into its components.
                string mimeCharSet = "", mimeContentDisposition = "", mimeContentID = "", mimeContentType = "", mimeContentTransferEncoding = "", mimeFileName = "";
                ExtractMimeHeaders(mimeHeaders, out mimeContentType, out mimeCharSet, out mimeContentTransferEncoding, out mimeContentDisposition, out mimeFileName, out mimeContentID);

                // Add the message to the MIME parts collection.
                mimeParts.Add(new MimePart(mimeFileName, string.IsNullOrEmpty(mimeContentType) ? contentType : mimeContentType, mimeCharSet, mimeContentID, body));
            }
            
            return mimeParts;
        }

        /// <summary>
        /// Extract the character set encoding from the content type.
        /// </summary>
        /// <param name="contentType">Content Type of the MIME part.</param>
        private static string GetCharSet(string contentType)
        {
            int charsetPos = contentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
            if (charsetPos > -1)
            {
                int charsetSemicolonPos = contentType.IndexOf(";", charsetPos + 8);
                if (charsetSemicolonPos > -1)
                    return contentType.Substring(charsetPos + 8, charsetSemicolonPos - charsetPos - 8);
                else
                    return contentType.Substring(charsetPos + 8);
            }
            return "";
        }

        /// <summary>
        /// Decrypt the encrypted S/MIME envelope.
        /// </summary>
        /// <param name="contentType">Content Type of the outermost MIME part.</param>
        /// <param name="contentTransferEncoding">Encoding of the outermost MIME part.</param>
        /// <param name="envelopeText">The MIME envelope.</param>
        /// <param name="processingFlags">Flags determining whether specialized properties are returned with a ReadOnlyMailMessage.</param>
        public static List<MimePart> ReturnDecryptedMimeParts(string contentType, string contentTransferEncoding, string envelopeText, ReadOnlyMailMessageProcessingFlags processingFlags)
        {
            try
            {
                // Hydrate the envelope CMS object.
                EnvelopedCms envelope = new EnvelopedCms();

                // Attempt to decrypt the envelope.
                envelope.Decode(Convert.FromBase64String(envelopeText));
                envelope.Decrypt();

                string body = Encoding.UTF8.GetString(envelope.ContentInfo.Content);
                string mimeHeaders = body.Substring(0, body.IndexOf("\r\n\r\n"));

                // Divide the MIME part's headers into its components.
                string mimeContentType = "", mimeCharSet = "", mimeContentTransferEncoding = "", mimeFileName = "", mimeContentDisposition = "", mimeContentID = "";
                ExtractMimeHeaders(mimeHeaders, out mimeContentType, out mimeCharSet, out mimeContentTransferEncoding, out mimeContentDisposition, out mimeFileName, out mimeContentID);

                // Recurse through embedded MIME parts.
                List<MimePart> mimeParts = ExtractMIMEParts(mimeContentType, mimeContentTransferEncoding, body, processingFlags);
                foreach (MimePart mimePart in mimeParts)
                    mimePart.SmimeEncryptedEnvelope = true;

                return mimeParts;
            }
            catch (Exception)
            {
                // If unable to decrypt the body, return null.
                return null;
            }
        }

        /// <summary>
        /// Verify the S/MIME signature.
        /// </summary>
        /// <param name="signatureBlock">The S/MIME signature block.</param>
        /// <param name="body">The message's raw body.</param>
        public static bool VerifySignature(string signatureBlock, string body)
        {
            // Strip trailing whitespace.
            if (signatureBlock.EndsWith("\r\n\r\n"))
                signatureBlock = signatureBlock.Substring(0, signatureBlock.Length - 4);

            // Hydrate the signature CMS object.
            ContentInfo contentInfo = new ContentInfo(Encoding.UTF8.GetBytes(body));
            SignedCms signedCms = new SignedCms(contentInfo);

            try
            {
                // Attempt to decode the signature block and verify the passed in signature.
                signedCms.Decode(Convert.FromBase64String(signatureBlock));
                signedCms.CheckSignature(true);

                return true;
            }
            catch (Exception)
            {
                // If an exception occured, the signature could not be verified.
                return false;
            }
        }

        /// <summary>
        /// Divide a MIME part's headers into its components.
        /// </summary>
        /// <param name="mimeHeaders">The raw headers portion of the MIME part.</param>
        /// <param name="mimeContentType">Content Type of the MIME part.</param>
        /// <param name="mimeCharset">Character Set used to encode the MIME part.</param>
        /// <param name="mimeContentDisposition">Content disposition, such as file metadata, of the MIME part.</param>
        /// <param name="mimeFileName">Filename of the MIME part.</param>
        /// <param name="mimeContentID">ID of the MIME part.</param>
        private static void ExtractMimeHeaders(string mimeHeaders, out string mimeContentType, out string mimeCharSet, out string mimeContentTransferEncoding, out string mimeContentDisposition, out string mimeFileName, out string mimeContentID)
        {
            // Initialize all headers as blank strings.
            mimeContentType = mimeCharSet = mimeContentTransferEncoding = mimeContentDisposition = mimeFileName = mimeContentID = "";

            // Record keeping variable to handle headers that spawn multiple lines.
            string lastMimeHeaderType = "";

            // Loop through each line of the headers.
            string[] mimeHeaderLines = mimeHeaders.Replace("\r", "").Split('\n');
            foreach (string header in mimeHeaderLines)
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

                    // Process each header's value based on its name.
                    switch (headerType)
                    {
                        case "content-disposition":
                            mimeContentDisposition = headerValue.Trim();
                            break;
                        case "content-id":
                            // Ignore opening and closing <> characters.
                            mimeContentID = headerValue.Trim();
                            if (mimeContentID.StartsWith("<"))
                                mimeContentID = mimeContentID.Substring(1);
                            if (mimeContentID.EndsWith(">"))
                                mimeContentID = mimeContentID.Substring(0, mimeContentID.Length - 1);
                            break;
                        case "content-transfer-encoding":
                            mimeContentTransferEncoding = headerValue.ToLower();
                            break;
                        case "content-type":
                            if (string.IsNullOrEmpty(mimeContentType))
                                mimeContentType = headerValue;
                            break;
                        default:
                            // Allow continuations if this header starts with whitespace.
                            headerProcessed = (!header.StartsWith("\t") && !header.StartsWith(" "));
                            break;
                    }
                    lastMimeHeaderType = headerType;
                }

                // Handle continuations for headers spanning multiple lines.
                if (!headerProcessed)
                {
                    switch (lastMimeHeaderType)
                    {
                        case "content-disposition":
                            mimeContentDisposition += header;
                            break;
                        case "content-type":
                            mimeContentType += header;
                            break;
                        default:
                            break;
                    }
                }
            }

            // If a content disposition has been specified, extract the filename.
            if (mimeContentDisposition.Length > 0)
                mimeFileName = Functions.ReturnBetween(mimeContentDisposition + ";", "name=", ";").Replace("\"", "");

            // If a content disposition has not been specified, search elsewhere in the content type string for the filename.
            if (string.IsNullOrEmpty(mimeFileName))
            {
                int nameStartPos = mimeContentType.IndexOf("name=", StringComparison.OrdinalIgnoreCase);
                if (nameStartPos > -1)
                {
                    int nameEndPos = mimeContentType.IndexOf(";", nameStartPos);
                    if (nameEndPos > -1)
                        mimeFileName = mimeContentType.Substring(nameStartPos + 5, nameEndPos - nameStartPos - 5);
                    else
                        mimeFileName = mimeContentType.Substring(nameStartPos + 5);

                    if (mimeFileName.StartsWith("\""))
                        mimeFileName = mimeFileName.Substring(1);
                    if (mimeFileName.EndsWith("\""))
                        mimeFileName = mimeFileName.Substring(0, mimeFileName.Length - 1);
                }
            }

            mimeCharSet = GetCharSet(mimeContentType);
        }
        #endregion Public Methods
    }
}
