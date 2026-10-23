namespace TenderSync.Infrastructure.Persistence.Seed;

/// <summary>The 14 IT interest categories from PLAN.md Appendix B. Keywords are starting points, editable in the dashboard.</summary>
internal static class InterestCategorySeed
{
    public sealed record Row(string Code, string Name, string Description, string[] Keywords, string[] NegativeKeywords);

    public static IReadOnlyList<Row> All { get; } =
    [
        new("SW_DEV", "Software Development", "Bespoke or custom software, system development and application development.",
            ["software development", "develop a system", "development of a system", "custom software", "application development", "bespoke software", "software solution", "system development"], []),
        new("WEB_DEV", "Web Development", "Websites, portals, intranets and content management systems.",
            ["website", "web portal", "web application", "intranet", "content management system", "cms", "redesign of website", "web development", "web site"], []),
        new("MOBILE", "Mobile Development", "Mobile applications for Android and iOS.",
            ["mobile app", "mobile application", "android", "ios app", "mobile development", "ussd"], []),
        new("APP_SUPPORT", "Application Support", "Support, maintenance and helpdesk for existing software systems.",
            ["application support", "system support", "support and maintenance of", "maintenance and support of", "helpdesk", "service desk", "service level agreement", "technical support of the system"],
            ["fire system", "security system", "irrigation system", "pa system", "sound system", "alarm system", "cctv", "access control system", "sprinkler", "hvac", "air conditioning", "ups", "generator"]),
        new("SYS_INT", "System Integration", "Integrating systems, interfaces, APIs, middleware and ERP platforms.",
            ["integration", "interface", "api", "middleware", "erp", "sap", "oracle ebs", "enterprise resource planning", "interoperability"], []),
        new("CLOUD", "Cloud Services", "Cloud hosting, migration and managed cloud platforms.",
            ["cloud", "azure", "aws", "amazon web services", "google cloud", "hosting", "migration to cloud", "saas", "iaas", "paas", "microsoft 365", "office 365"], []),
        new("AI_ML", "AI / Machine Learning", "Artificial intelligence, machine learning, chatbots and process automation.",
            ["artificial intelligence", "machine learning", "chatbot", "automation", "rpa", "robotic process automation", "natural language", "predictive"], []),
        new("DATA_ANALYTICS", "Data Analytics", "Data engineering, warehousing, ETL and analytics platforms.",
            ["data analytics", "data warehouse", "etl", "data engineering", "data management", "data platform", "data lake", "big data", "data migration"], []),
        new("BI", "Business Intelligence", "Reporting, dashboards and business intelligence tooling.",
            ["business intelligence", "power bi", "dashboards", "reporting solution", "reporting system", "sage bi", "qlik", "tableau", "management information system"], []),
        new("IT_CONSULT", "IT Consulting", "ICT strategy, assessments, enterprise architecture and advisory.",
            ["ict consulting", "ict strategy", "ict assessment", "enterprise architecture", "it advisory", "ict advisory", "digital transformation", "ict governance", "ict audit", "it consulting", "ict master plan"], []),
        new("SW_MAINT", "Software Maintenance", "Maintenance, upgrades, enhancements and licence support for software.",
            ["software maintenance", "licence renewal and support", "license renewal and support", "upgrade of system", "enhancements", "software licence", "software license", "subscription renewal", "annual maintenance"], []),
        new("DEVOPS", "DevOps", "CI/CD, containers, Kubernetes and platform engineering.",
            ["devops", "ci/cd", "continuous integration", "containerisation", "containerization", "kubernetes", "docker", "platform engineering"], []),
        new("CYBER", "Cyber Security", "Information security, penetration testing, SOC and security compliance.",
            ["cyber security", "cybersecurity", "information security", "penetration testing", "soc", "security operations centre", "vulnerability assessment", "iso 27001", "siem", "endpoint protection", "firewall"],
            ["physical security", "guarding", "security guards", "security services", "cctv only", "armed response"]),
        new("DB_DEV", "Database Development", "Database design, administration, migration and development.",
            ["database", "sql server", "oracle database", "postgresql", "mysql", "data migration", "database administration", "dba"], []),
    ];
}
