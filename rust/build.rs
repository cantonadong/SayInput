use std::{env, fs, path::PathBuf, process::Command};
fn main() {
    let root = PathBuf::from(env::var_os("CARGO_MANIFEST_DIR").unwrap());
    let out = PathBuf::from(env::var_os("OUT_DIR").unwrap());
    let icon = root.join("../icon/icon.ico").canonicalize().unwrap();
    let mut status = Vec::new();
    for name in ["rec", "process"] {
        let source = root.join(format!("../icon/{name}.png"));
        println!("cargo:rerun-if-changed={}", source.display());
        let png = fs::read(source).unwrap();
        assert_eq!(&png[..8], b"\x89PNG\r\n\x1a\n");
        let width = u32::from_be_bytes(png[16..20].try_into().unwrap());
        let height = u32::from_be_bytes(png[20..24].try_into().unwrap());
        assert!(
            width <= 256 && height <= 256,
            "Current status asset must be a single icon"
        );
        let mut ico = vec![
            0,
            0,
            1,
            0,
            1,
            0,
            width as u8,
            height as u8,
            0,
            0,
            1,
            0,
            32,
            0,
        ];
        ico.extend((png.len() as u32).to_le_bytes());
        ico.extend(22u32.to_le_bytes());
        ico.extend(png);
        let target = out.join(format!("{name}.ico"));
        fs::write(&target, ico).unwrap();
        status.push(target);
    }
    let quote = |p: &std::path::Path| p.to_string_lossy().replace('\\', "/").replace("//?/", "");
    let rc = format!(
        r#"
1 ICON "{}"
2 ICON "{}"
3 ICON "{}"
1 24 "{}"
1 VERSIONINFO
FILEVERSION 1,5,0,0
PRODUCTVERSION 1,5,0,0
FILEFLAGSMASK 0x3fL
FILEOS 0x40004L
FILETYPE 0x1L
BEGIN
 BLOCK "StringFileInfo"
 BEGIN
  BLOCK "040904b0"
  BEGIN
   VALUE "CompanyName", "SayInput"
   VALUE "FileDescription", "SayInput - Native voice input"
   VALUE "FileVersion", "1.5.0.0"
   VALUE "InternalName", "SayInput"
   VALUE "OriginalFilename", "SayInput.exe"
   VALUE "ProductName", "SayInput"
   VALUE "ProductVersion", "1.5.0 Rust"
  END
 END
 BLOCK "VarFileInfo"
 BEGIN
  VALUE "Translation", 0x0409, 1200
 END
END
"#,
        quote(&icon),
        quote(&status[0]),
        quote(&status[1]),
        quote(&root.join("app.manifest"))
    );
    let resource = out.join("resources.rc");
    fs::write(&resource, rc).unwrap();
    let object = out.join("resources.o");
    let tool = env::var_os("SAYINPUT_WINDRES").unwrap_or_else(|| "windres.exe".into());
    let result = Command::new(tool)
        .args([
            "--input-format=rc",
            "--output-format=coff",
            "--target=pe-x86-64",
            "--codepage=65001",
        ])
        .arg("-i")
        .arg(&resource)
        .arg("-o")
        .arg(&object)
        .status()
        .expect("Windows resource compiler required");
    assert!(result.success(), "Resource compilation failed");
    println!("cargo:rustc-link-arg={}", object.display());
    println!("cargo:rerun-if-changed=app.manifest");
    println!("cargo:rerun-if-changed={}", icon.display());
    println!("cargo:rerun-if-env-changed=SAYINPUT_WINDRES");
}
