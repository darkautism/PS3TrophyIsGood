fn main() {
    println!("cargo:rerun-if-changed=assets/ps3trophyisgood.ico");
    #[cfg(target_os = "windows")]
    winres::WindowsResource::new()
        .set_icon("assets/ps3trophyisgood.ico")
        .compile()
        .expect("could not embed original PS3TrophyIsGood icon");
}
