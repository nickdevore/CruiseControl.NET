using System;
using System.Collections;
using System.Net.Mail;
using Exortech.NetReflector;
using ThoughtWorks.CruiseControl.Core.Util;
using ThoughtWorks.CruiseControl.Remote;
using ThoughtWorks.CruiseControl.Core.Config;
using ThoughtWorks.CruiseControl.Core.Tasks;
using System.IO;

namespace ThoughtWorks.CruiseControl.Core.Publishers
{
    /// <summary>
    /// Publishes results of integrations via email.  This implementation supports
    /// plain-text, and Html email formats.  Rules regarding who receives email
    /// are configurable.
    /// </summary>
    [ReflectorType("email")]
    public class EmailPublisher 
        : TaskBase, IConfigurationValidation
    {
        private EmailGateway emailGateway = new EmailGateway();
        private string fromAddress;
        private string replytoAddress;
        private string subjectPrefix;
        private Hashtable users = new Hashtable();
        private Hashtable groups = new Hashtable();
        private IMessageBuilder messageBuilder;
        private EmailGroup.NotificationType[] modifierNotificationTypes = { EmailGroup.NotificationType.Always };
        private IEmailConverter[] converters = new IEmailConverter[0];

        private Hashtable subjectSettings = new Hashtable();

        private string[] xslFiles;

        public EmailPublisher()
            : this(new HtmlLinkMessageBuilder(false))
        { }

        public EmailPublisher(IMessageBuilder messageBuilder)
        {
            this.messageBuilder = messageBuilder;
        }

        public EmailGateway EmailGateway
        {
            get { return emailGateway; }
            set { emailGateway = value; }
        }

        public IMessageBuilder MessageBuilder
        {
            get { return messageBuilder; }
            set { messageBuilder = value; }
        }

        /// <summary>
        /// The host name of the mail server.  This field is required to send email notifications.
        /// </summary>
        [ReflectorProperty("mailhost")]
        public string MailHost
        {
            get { return EmailGateway.MailHost; }
            set { EmailGateway.MailHost = value; }
        }

        /// <summary>
        /// The port number of the mail server.
        /// </summary>
        /// <remarks>
        /// Optional, defaults to port 25 (via the default of System.Net.Mail.SmtpClient).
        /// </remarks>
        [ReflectorProperty("mailport", Required = false)]
        public int MailPort
        {
            get { return EmailGateway.MailPort; }
            set { EmailGateway.MailPort = value; }
        }

        [ReflectorProperty("mailhostUsername", Required = false)]
        public string MailhostUsername
        {
            get { return EmailGateway.MailHostUsername; }
            set { EmailGateway.MailHostUsername = value; }
        }

        [ReflectorProperty("mailhostPassword", Required = false)]
        public string MailhostPassword
        {
            get { return EmailGateway.MailHostPassword; }
            set { EmailGateway.MailHostPassword = value; }
        }

        /// <summary>
        /// The email address from which build results appear to have originated from.  This
        /// value seems to be required for most mail servers.
        /// </summary>
        [ReflectorProperty("from")]
        public string FromAddress
        {
            get { return fromAddress; }
            set { fromAddress = value; }
        }

        /// <summary>
        /// Use SSL or not, defaults to false
        /// </summary>
        [ReflectorProperty("useSSL", Required = false)]
        public bool UseSSL
        {
            get { return EmailGateway.UseSSL; }
            set { EmailGateway.UseSSL = value; }
        }

        [ReflectorProperty("replyto", Required = false)]
        public string ReplyToAddress
        {
            get { return replytoAddress; }
            set { replytoAddress = value; }
        }


        [ReflectorProperty("xslFiles", Required = false)]
        public string[] XslFiles
        {
            get { return xslFiles; }
            set { xslFiles = value; }
        }

        [ReflectorProperty("attachments", Required = false)]
        public string[] Attachments { get; set; }

        /// <summary>
        /// Set this property (in configuration) to enable HTML emails containing build details.
        /// This property is deprecated and should be removed.  It should be replaced by the MessageBuilder property.
        /// </summary>
        [ReflectorProperty("includeDetails", Required = false)]
        public bool IncludeDetails
        {
            get
            {
                return messageBuilder is HtmlDetailsMessageBuilder;
            }
            set
            {
                if (value)
                {
                    messageBuilder = new HtmlDetailsMessageBuilder();
                }
                else
                {
                    messageBuilder = new HtmlLinkMessageBuilder(false);
                }
            }
        }

        /// <summary>
        /// Send an email to the modifiers of the build on this notification type
        /// This allows for example to send a mail to the modifiers only when the build breaks
        /// notification type = Failed
        /// </summary>
        [ReflectorProperty("modifierNotificationTypes", Required = false)]
        public EmailGroup.NotificationType[] ModifierNotificationTypes
        {
            get { return modifierNotificationTypes; }
            set { modifierNotificationTypes = value; }
        }


        [ReflectorHash("users", "name")]
        public Hashtable EmailUsers
        {
            get { return users; }
            set { users = value; }
        }

        [ReflectorHash("groups", "name")]
        public Hashtable EmailGroups
        {
            get { return groups; }
            set { groups = value; }
        }


        [ReflectorHash("subjectSettings", "buildResult", Required = false)]
        public Hashtable SubjectSettings
        {
            get { return subjectSettings; }
            set { subjectSettings = value; }
        }

        /// <summary>
        /// Allows transformations to be performed on the names of the modifiers for making an email address.
        /// This way, it is not necessary to include all users on a project in the users tag of the emailpublisher.
        /// </summary>
        [ReflectorArray("converters", Required = false)]
        public IEmailConverter[] Converters
        {
            get { return converters; }
            set { converters = value; }
        }


        [ReflectorProperty("subjectPrefix", Required = false)]
        public string SubjectPrefix
        {
            get { return subjectPrefix; }
            set { subjectPrefix = value; }
        }

        protected override bool Execute(IIntegrationResult result)
        {
            if (result.Status == IntegrationStatus.Unknown) return false;

            result.BuildProgressInformation.SignalStartRunTask(!string.IsNullOrEmpty(Description) ? Description : "Emailing ...");

            EmailMessage emailMessage = new EmailMessage(result, this);
            string to = emailMessage.Recipients;
            string subject = emailMessage.Subject;
            string message = CreateMessage(result);
            if (IsRecipientSpecified(to))
            {
                Log.Info(string.Format("Emailing \"{0}\" to {1}", subject, to));
                SendMessage(fromAddress, to, replytoAddress, subject, message, result.WorkingDirectory);
            }

            return true;
        }

        private static bool IsRecipientSpecified(string to)
        {
            return to != null && to.Trim() != string.Empty;
        }

        public virtual void SendMessage(string from, string to, string replyto, string subject, string message, string workingFolder)
        {
            try
            {
                using (var actualMessage = GetMailMessage(from, to, replyto, subject, message, workingFolder, Attachments))
                {
                    emailGateway.Send(actualMessage);
                }
            }
            catch (Exception e)
            {
                throw new CruiseControlException("EmailPublisher exception: " + e, e);
            }
        }

        protected static MailMessage GetMailMessage(string from, string to, string replyto, string subject, string messageText, string workingFolder, string[] attachments)
        {
            MailMessage mailMessage = new MailMessage();
            mailMessage.To.Add(to);
            mailMessage.From = new MailAddress(from);
            if (!String.IsNullOrEmpty(replyto)) mailMessage.ReplyTo = new MailAddress(replyto);
            mailMessage.Subject = subject;
            mailMessage.IsBodyHtml = true;
            mailMessage.Body = messageText;

            // Add any attachments
            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    var fullPath = attachment;
                    if (!Path.IsPathRooted(fullPath)) fullPath = Path.Combine(workingFolder, fullPath);
                    if (File.Exists(fullPath))
                    {
                        var mailAttachment = new Attachment(fullPath);
                        mailMessage.Attachments.Add(mailAttachment);
                    }
                }
            }

            return mailMessage;
        }

        public string CreateMessage(IIntegrationResult result)
        {
            // TODO Add culprit to message text -- especially if modifier is not an email user
            //      This information is included, when using Html email (all mods are shown)
            try
            {
                messageBuilder.xslFiles = this.XslFiles;
                return messageBuilder.BuildMessage(result);
            }
            catch (Exception e)
            {
                string message = "Unable to build email message: " + e;
                Log.Error(message);
                return message;
            }
        }

        #region Validate()
        /// <summary>
        /// Checks the internal validation of the item.
        /// </summary>
        /// <param name="configuration">The entire configuration.</param>
        /// <param name="parent">The parent item for the item being validated.</param>
        public virtual void Validate(IConfiguration configuration, object parent, IConfigurationErrorProcesser errorProcesser)
        {
            if (parent is Project)
            {
                Project parentProject = parent as Project;

                // Attempt to find this publisher in the publishers section
                bool isPublisher = false;
                foreach (ITask task in parentProject.Publishers)
                {
                    if (task == this)
                    {
                        isPublisher = true;
                        break;
                    }
                }

                // If not found then throw a validation exception
                if (!isPublisher)
                {
                    errorProcesser.ProcessWarning("Email publishers are best placed in the publishers section of the configuration");
                }
            }
            else
            {
                errorProcesser.ProcessError(
                    new CruiseControlException("This publisher can only belong to a project"));
            }
        }
        #endregion
    }
}
