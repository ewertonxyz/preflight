namespace Preflight.Abstractions.Model;

/// <summary>
/// How much room a volume has, as the host measured it.
/// </summary>
/// <param name="VolumeName">The volume as the operating system names it.</param>
/// <param name="TotalBytes">The volume's capacity.</param>
/// <param name="AvailableBytes">What is free to the account running the tool.</param>
/// <remarks>
/// Available rather than free, and the two differ on every filesystem with a
/// quota or a reserve: what decides whether a build fits is what this account
/// may write, not what the disk technically holds.
/// </remarks>
public sealed record VolumeSpace(string VolumeName, long TotalBytes, long AvailableBytes);
