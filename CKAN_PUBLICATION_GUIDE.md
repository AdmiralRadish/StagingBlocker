# CKAN Publication Guide for Staging Blocker

## ✅ Completed Steps

The following have been set up for CKAN publication:

### 1. **Clean Release Build**
- ✅ DLL compiled in Release mode
- ✅ DLL copied to `GameData/StagingBlocker/Plugins/StagingBlocker.dll`
- ✅ Ready for distribution

### 2. **Project Documentation**
- ✅ `README.md` - Complete feature documentation and usage guide
- ✅ `CHANGELOG.md` - Version history and feature list
- ✅ `LICENSE` - MIT License file
- ✅ Proper folder structure in GameData

### 3. **CKAN Metadata**
- ✅ `StagingBlocker.netkan` - NetKAN file for automatic CKAN metadata generation

## 📋 Next Steps to Get Published on CKAN

### Step 1: Create a GitHub Release

1. Ensure your code is on GitHub: `https://github.com/nickbaumann/StagingBlocker`
2. Tag a release:
   ```powershell
   git tag -a v1.0.0 -m "Initial release of Staging Blocker"
   git push origin v1.0.0
   ```

3. Go to GitHub → Releases → Create New Release
   - Tag: `v1.0.0`
   - Title: `Staging Blocker v1.0.0`
   - Description: Copy from README.md feature list
   - Upload files:
     - Create a ZIP of the GameData folder contents (the DLL and any assets)
     - Name it: `StagingBlocker-1.0.0.zip`
     - Attach to the release

### Step 2: Submit to CKAN via NetKAN

1. Fork [CKAN-meta](https://github.com/KSP-CKAN/CKAN-meta)
2. Create a new directory: `master/StagingBlocker/`
3. Copy your `StagingBlocker.netkan` file into that directory
4. Commit and push:
   ```powershell
   git add master/StagingBlocker/StagingBlocker.netkan
   git commit -m "Add StagingBlocker to CKAN"
   git push origin master
   ```

5. Create a Pull Request on CKAN-meta
   - Title: "Add StagingBlocker v1.0.0"
   - Description: "Initial submission of Staging Blocker mod"
   - CKAN maintainers will review and merge

### Step 3: Wait for Processing

- CKAN's automated system will:
  1. Validate your `.netkan` file
  2. Download your GitHub release
  3. Create the final `.ckan` metadata
  4. Add it to CKAN's mod list

- This typically takes 1-2 hours after PR approval

## 🔍 Important Notes

### Before Submitting:
- ✅ Verify the GitHub repository URL is correct in `StagingBlocker.netkan`
- ✅ Ensure version number in `.netkan` matches your GitHub tag
- ✅ Test that the release ZIP installs correctly into a fresh KSP

### File Structure in Release ZIP Should Be:
```
StagingBlocker/
  ├── Plugins/
  │   └── StagingBlocker.dll
  └── Textures/
      └── icon.png (and any other textures)
```

### KSP Version Compatibility:
Currently set to: **KSP 1.8 - 1.12**
- Update `ksp_version_max` in `.netkan` if you test newer versions
- Update `ksp_version_min` if you ensure compatibility with older versions

## 📝 Updating in the Future

When you release a new version:

1. Build and test locally
2. Tag a new GitHub release: `v1.1.0`
3. CKAN will automatically generate the new `.ckan` file
4. No need to resubmit to CKAN-meta (unless your `.netkan` changes)

## 🆘 Support Resources

- [CKAN Documentation](https://github.com/KSP-CKAN/CKAN/wiki)
- [NetKAN Spec](https://github.com/KSP-CKAN/CKAN/blob/master/Spec.md)
- [CKAN Meta Readme](https://github.com/KSP-CKAN/CKAN-meta/blob/master/README.md)
- CKAN IRC: `#ksp-ckan` on Libera.Chat

## ⚡ Quick Checklist

Before publishing:

- [ ] GitHub repository created and public
- [ ] Code committed to master branch
- [ ] Version 1.0.0 tagged on GitHub
- [ ] Release ZIP created with correct folder structure
- [ ] README.md complete and helpful
- [ ] CHANGELOG.md has release notes
- [ ] LICENSE file present
- [ ] StagingBlocker.netkan has correct GitHub URLs
- [ ] Tested fresh install in clean KSP installation
- [ ] CKAN-meta fork ready for pull request

## Total Time Estimate

- **Setup (completed)**: ~15 minutes ✅
- **GitHub release**: ~5 minutes
- **CKAN submission**: ~10 minutes
- **CKAN review/processing**: 1-2 hours
- **Total**: ~2-3 hours until publicly listed on CKAN

Good luck with your submission! 🚀
