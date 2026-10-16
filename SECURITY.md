# Security policy

Use GitHub's private vulnerability reporting for security-sensitive reports once enabled for this repository: **Security → Report a vulnerability**. If that option is unavailable, open an issue requesting a private reporting channel without disclosing exploit details, sensitive data, or credentials.

Include the affected version, runtime/OS, minimal synthetic reproduction, impact, and any mitigations. No response-time guarantee is offered. The latest published OSS version is the supported release line.

Abraxas runs inside the application process and follows its filesystem permissions. The OSS edition does not encrypt stored records or index keys. Applications own access control, trusted object/hydration configuration, backup protection, and resource limits for external input. See the guides for record-level durability and recovery contracts; these are not cross-record transactions.
