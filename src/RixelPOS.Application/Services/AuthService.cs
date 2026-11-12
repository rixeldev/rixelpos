using RixelPOS.Application.Abstractions;
using RixelPOS.Application.Sessions;
using RixelPOS.Domain.Common;
using RixelPOS.Domain.Entities;
using RixelPOS.Domain.Enums;
using RixelPOS.Domain.Security;

namespace RixelPOS.Application.Services;

public sealed class AuditService
{
    private readonly IPosStore _store;
    private readonly IClock _clock;

    public AuditService(IPosStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public void Record(User? user, string action, string entityType, string? entityId = null, string? details = null)
    {
        _store.AddAudit(new AuditEntry
        {
            Id = Guid.NewGuid(),
            UserId = user?.Id,
            UserName = user?.FullName ?? "system",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            CreatedAt = _clock.UtcNow
        });
    }
}

public sealed class AuthService
{
    private readonly IPosStore _store;
    private readonly IPasswordHasher _hasher;
    private readonly AppSession _session;
    private readonly AuditService _audit;
    private readonly IAppLogger _logger;

    public AuthService(IPosStore store, IPasswordHasher hasher, AppSession session, AuditService audit, IAppLogger logger)
    {
        _store = store;
        _hasher = hasher;
        _session = session;
        _audit = audit;
        _logger = logger;
    }

    public IReadOnlyList<User> GetActiveUsers() => _store.GetUsers().Where(u => u.IsActive).ToList();

    public User? AuthenticatePin(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return null;
        }

        foreach (var user in _store.GetUsersWithPin().Where(u => u.IsActive))
        {
            if (_hasher.VerifyPin(pin, user.PinHash))
            {
                _logger.Info($"PIN login OK for {user.Username}");
                return user;
            }
        }

        _logger.Warn("Failed PIN login attempt");
        return null;
    }

    public User? AuthenticatePassword(string username, string password)
    {
        var user = _store.FindUserByUsername(username.Trim());
        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.PasswordHash))
        {
            _logger.Warn($"Failed password login for '{username}'");
            return null;
        }

        return _hasher.VerifyPassword(password, user.PasswordHash) ? user : null;
    }

    public void SignIn(User user)
    {
        _session.SignIn(user);
        _audit.Record(user, "auth.login", "user", user.Id.ToString());
    }

    public void SignOut()
    {
        var user = _session.CurrentUser;
        _session.SignOut();
        if (user is not null)
        {
            _audit.Record(user, "auth.logout", "user", user.Id.ToString());
        }
    }

    public bool ApproveManager(string pin, Permission permission)
    {
        foreach (var user in _store.GetUsersWithPin().Where(u => u.IsActive && u.Role is UserRole.Manager or UserRole.Admin))
        {
            if (_hasher.VerifyPin(pin, user.PinHash) && RolePermissions.Can(user.Role, permission))
            {
                _audit.Record(user, "auth.approval", "permission", permission.ToString());
                return true;
            }
        }

        return false;
    }

    public User CreateUser(string username, string fullName, UserRole role, string? pin, string? password)
    {
        username = NormalizeUsername(username);
        ValidateUserInput(username, fullName, role, pin, password, isNew: true);

        var user = new User
        {
            Username = username,
            FullName = fullName.Trim(),
            Role = role,
            PinHash = string.IsNullOrWhiteSpace(pin) ? null : _hasher.HashPin(pin),
            PasswordHash = string.IsNullOrWhiteSpace(password) ? null : _hasher.HashPassword(password),
            IsActive = true
        };

        _store.SaveUser(user);
        _audit.Record(_session.CurrentUser, "user.create", "user", user.Id.ToString(), $"role={role}");
        return user;
    }

    public void UpdateUser(User user, string? newPin = null, string? newPassword = null, bool clearPin = false)
    {
        if (string.IsNullOrWhiteSpace(user.FullName))
        {
            throw new BusinessRuleException("user.name_required", "Full name is required.");
        }

        if (string.IsNullOrWhiteSpace(user.Username) || user.Username.Trim().Length < 3)
        {
            throw new BusinessRuleException("user.username_short", "Username must have at least 3 characters.");
        }

        var duplicate = _store.FindUserByUsername(user.Username.Trim());
        if (duplicate is not null && duplicate.Id != user.Id)
        {
            throw new BusinessRuleException("user.username_exists", "That username is already in use.");
        }

        user.Username = user.Username.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(newPin))
        {
            ValidatePin(newPin);
            user.PinHash = _hasher.HashPin(newPin);
        }
        else if (clearPin)
        {
            user.PinHash = null;
        }

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            ValidatePassword(newPassword, user.Role);
            user.PasswordHash = _hasher.HashPassword(newPassword);
        }

        user.UpdatedAt = DateTime.UtcNow;
        _store.SaveUser(user);
        _audit.Record(_session.CurrentUser, "user.update", "user", user.Id.ToString());
    }

    public void SetPin(Guid userId, string pin)
    {
        var user = _store.GetUser(userId) ?? throw new BusinessRuleException("user.not_found", "User not found.");
        ValidatePin(pin);
        user.PinHash = _hasher.HashPin(pin);
        user.UpdatedAt = DateTime.UtcNow;
        _store.SaveUser(user);
        _audit.Record(_session.CurrentUser, "user.pin_change", "user", user.Id.ToString());
    }

    public void SetActive(Guid userId, bool active)
    {
        var user = _store.GetUser(userId) ?? throw new BusinessRuleException("user.not_found", "User not found.");
        if (!active && user.Role == UserRole.Admin && _store.CountAdmins() <= 1)
        {
            throw new BusinessRuleException("user.last_admin", "You cannot deactivate the last administrator.");
        }

        user.IsActive = active;
        user.UpdatedAt = DateTime.UtcNow;
        _store.SaveUser(user);
        _audit.Record(_session.CurrentUser, active ? "user.activate" : "user.deactivate", "user", user.Id.ToString());
    }

    private void ValidateUserInput(string username, string fullName, UserRole role, string? pin, string? password, bool isNew)
    {
        if (username.Length < 3)
        {
            throw new BusinessRuleException("user.username_short", "Username must have at least 3 characters.");
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new BusinessRuleException("user.name_required", "Full name is required.");
        }

        if (isNew && _store.FindUserByUsername(username) is not null)
        {
            throw new BusinessRuleException("user.username_exists", "That username is already in use.");
        }

        if (string.IsNullOrWhiteSpace(pin) && string.IsNullOrWhiteSpace(password))
        {
            throw new BusinessRuleException("user.credentials_required", "Add a PIN or a password.");
        }

        if (!string.IsNullOrWhiteSpace(pin))
        {
            ValidatePin(pin);
        }

        if (!string.IsNullOrWhiteSpace(password))
        {
            ValidatePassword(password, role);
        }
    }

    private static void ValidatePin(string pin)
    {
        if (pin.Length is < 4 or > 8 || !pin.All(char.IsDigit))
        {
            throw new BusinessRuleException("user.invalid_pin", "The PIN must contain 4 to 8 digits.");
        }
    }

    private static void ValidatePassword(string password, UserRole role)
    {
        if (password.Length < 8)
        {
            throw new BusinessRuleException("user.password_short", "The password must have at least 8 characters.");
        }

        if (role is UserRole.Admin or UserRole.Manager && !password.Any(char.IsDigit))
        {
            throw new BusinessRuleException("user.password_weak", "Manager passwords must include at least one number.");
        }
    }

    private static string NormalizeUsername(string username) =>
        username.Trim().ToLowerInvariant().Replace(' ', '.');
}
