using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProcessCoreOptimizer.WPF.Models;
using ProcessCoreOptimizer.WPF.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProcessCoreOptimizer.Tests;

[TestClass]
public sealed class ProfileStoreIdentityTests
{
    private static ProcessProfile Make(string name, string? path = null, long mask = 0xFF)
    {
        return new ProcessProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            ProcessName = name,
            ExecutablePath = path,
            AffinityMask = mask,
            Priority = "Normal",
            ApplyCoreOptimization = true,
            ApplyPriority = true,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    [TestMethod]
    public void ReplaceNameOnly_PreservesPathBoundSiblings()
    {
        var pathBound = Make("myapp", @"C:\Tools\myapp.exe");
        var nameOnly = Make("myapp");
        var current = new List<ProcessProfile> { pathBound, nameOnly };

        var incoming = Make("myapp");
        var result = ProfileService.ReplaceNameOnlyProfile(current, incoming);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.Any(p => p.Id == pathBound.Id), "Path-bound sibling must survive a name-only save.");
        Assert.IsTrue(result.Any(p => p.Id == incoming.Id));
        Assert.IsFalse(result.Any(p => p.Id == nameOnly.Id), "Previous name-only slot must be replaced.");
    }

    [TestMethod]
    public void ReplacePathBound_ReplacesOnlySamePathSlot()
    {
        var first = Make("myapp", @"C:\A\myapp.exe");
        var second = Make("myapp", @"C:\B\myapp.exe");
        var nameOnly = Make("myapp");
        var current = new List<ProcessProfile> { first, second, nameOnly };

        var incoming = Make("myapp", @"C:\B\myapp.exe", mask: 0x0F);
        var result = ProfileService.ReplacePathBoundOrAdd(current, incoming);

        Assert.AreEqual(3, result.Count);
        Assert.IsTrue(result.Any(p => p.Id == first.Id));
        Assert.IsTrue(result.Any(p => p.Id == nameOnly.Id));
        Assert.IsTrue(result.Any(p => p.Id == incoming.Id));
        Assert.IsFalse(result.Any(p => p.Id == second.Id));
    }

    [TestMethod]
    public void RemoveById_RemovesExactlyOneAmongDuplicates()
    {
        var first = Make("myapp");
        var second = Make("myapp", @"C:\B\myapp.exe");
        var other = Make("other");
        var current = new List<ProcessProfile> { first, second, other };

        var result = ProfileService.RemoveById(current, first.Id);

        Assert.AreEqual(2, result.Count);
        Assert.IsFalse(result.Any(p => p.Id == first.Id));
        Assert.IsTrue(result.Any(p => p.Id == second.Id));
        Assert.IsTrue(result.Any(p => p.Id == other.Id));
    }

    [TestMethod]
    public void RemoveById_UnknownOrEmptyId_RemovesNothing()
    {
        var first = Make("myapp");
        var current = new List<ProcessProfile> { first };

        Assert.AreEqual(1, ProfileService.RemoveById(current, "deadbeef").Count);
        Assert.AreEqual(1, ProfileService.RemoveById(current, null).Count);
        Assert.AreEqual(1, ProfileService.RemoveById(current, "  ").Count);
    }

    [TestMethod]
    public void FindById_ReturnsCorrectSibling()
    {
        var first = Make("myapp");
        var second = Make("myapp", @"C:\B\myapp.exe");
        var current = new List<ProcessProfile> { first, second };

        Assert.AreSame(second, ProfileService.FindById(current, second.Id));
        Assert.IsNull(ProfileService.FindById(current, null));
        Assert.IsNull(ProfileService.FindById(current, "nope"));
    }
}
