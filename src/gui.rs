//! Pure Rust GPUI frontend. No WebView, JS, Tauri, or advertisements.
use gpui::{
    App, Bounds, Context, Render, Window, WindowBounds, WindowOptions,
    div, prelude::*, px, rgb, size, uniform_list,
};
use gpui_platform::application;
use gpui_ce_elements::editable_text::{
    EditableTextState, StringStorage, text_input,
    actions::{default_bindings, DEFAULT_INPUT_CONTEXT},
};
use ps3trophyisgood::{
    SortKey, TrophySession, format_timestamp, CopySource, SmartCopyOptions,
    load_copy_page, parse_copy_page, map_remote_to_local, smart_copy,
};
use std::collections::HashMap;
use std::sync::Arc;

const BG: u32 = 0x0f141c;
const SURFACE: u32 = 0x19212d;
const HOVER: u32 = 0x233044;
const MUTED: u32 = 0x93a2b5;
const TEXT: u32 = 0xe9eff8;
const ACCENT: u32 = 0x7eb6ff;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Filter {
    All,
    Obtained,
    Locked,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum ToolPanel {
    None,
    Batch,
    CopyFrom,
}

pub struct TrophyApp {
    session: Option<TrophySession>,
    icons: HashMap<u32, Arc<gpui::Image>>,
    game_icon: Option<Arc<gpui::Image>>,
    visible: Vec<usize>,
    selected_id: Option<u32>,
    sort: SortKey,
    descending: bool,
    filter: Filter,
    status: String,
    dirty: bool,
    time_input: gpui::Entity<EditableTextState>,
    random_start_input: gpui::Entity<EditableTextState>,
    random_end_input: gpui::Entity<EditableTextState>,
    copy_url_input: gpui::Entity<EditableTextState>,
    smart_options_input: gpui::Entity<EditableTextState>,
    smart_copy_enabled: bool,
    is_copying: bool,
    tool_panel: ToolPanel,
}

impl TrophyApp {
    fn new(cx: &mut Context<Self>) -> Self {
        let now = chrono::Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Secs, true);
        let time_input = cx.new(|cx| EditableTextState::new(StringStorage::from(now.clone()), cx));
        let random_start_input = cx.new(|cx| EditableTextState::new(
            StringStorage::from("2008-01-01T00:00:00Z"), cx));
        let random_end_input = cx.new(|cx| EditableTextState::new(StringStorage::from(now), cx));
        let copy_url_input = cx.new(|cx| EditableTextState::new(StringStorage::default(), cx));
        let smart_options_input = cx.new(|cx| EditableTextState::new(
            StringStorage::from("0,0,0,0,0"), cx));
        let mut app = Self {
            session: None,
            icons: HashMap::new(),
            game_icon: None,
            visible: Vec::new(),
            selected_id: None,
            sort: SortKey::Id,
            descending: false,
            filter: Filter::All,
            status: "Drag a PS3 trophy folder here, or click Open Folder.".into(),
            dirty: false,
            time_input,
            random_start_input,
            random_end_input,
            copy_url_input,
            smart_options_input,
            smart_copy_enabled: false,
            is_copying: false,
            tool_panel: ToolPanel::None,
        };
        if let Some(folder) = std::env::args_os().nth(1) {
            app.open_folder(std::path::PathBuf::from(folder));
        }
        app
    }

    fn confirm_unsaved(&mut self) -> bool {
        if !self.dirty {
            return true;
        }
        use rfd::{MessageButtons, MessageDialog, MessageDialogResult};
        match MessageDialog::new()
            .set_title("Unsaved trophy edits")
            .set_description("Save changes to the currently opened trophy set before closing?")
            .set_buttons(MessageButtons::YesNoCancel)
            .show()
        {
            MessageDialogResult::Yes => {
                if let Some(session) = self.session.as_ref() {
                    if let Err(error) = session.save_with_backup() {
                        self.status = format!("Unable to save current trophy set: {error:#}");
                        return false;
                    }
                }
                self.dirty = false;
                true
            }
            MessageDialogResult::No => true,
            _ => false,
        }
    }

    fn open_folder(&mut self, path: std::path::PathBuf) {
        if !self.confirm_unsaved() {
            return;
        }
        match TrophySession::open(&path) {
            Ok(session) => {
                let total = session.set.trophies.len();
                let format = session.set.format.label();
                self.selected_id = None;
                self.dirty = false;
                self.icons.clear();
                self.game_icon = std::fs::read(path.join("ICON0.PNG")).ok().map(|bytes|
                    Arc::new(gpui::Image::from_bytes(gpui::ImageFormat::Png, bytes)));
                // Cache each PNG once, keyed by the real trophy ID rather than
                // the row position (which changes when sorting or filtering).
                for trophy in &session.set.trophies {
                    let icon_path = path.join(format!("TROP{:03}.PNG", trophy.id));
                    if let Ok(bytes) = std::fs::read(&icon_path) {
                        self.icons.insert(trophy.id, Arc::new(
                            gpui::Image::from_bytes(gpui::ImageFormat::Png, bytes)));
                    }
                }
                self.status = format!("{format} | {total} trophies | {} | {}",
                    session.set.npcommid, path.display());
                self.session = Some(session);
                self.rebuild();
            }
            Err(e) => {
                self.status = format!("Could not open {}: {e:#}", path.display());
            }
        }
    }

    fn refresh_folder(&mut self, cx: &mut Context<Self>) {
        if let Some(path) = self.session.as_ref().map(|s| s.source.clone()) {
            self.open_folder(path);
        }
        cx.notify();
    }

    fn close_folder(&mut self, cx: &mut Context<Self>) {
        if self.confirm_unsaved() {
            self.session = None;
            self.icons.clear();
            self.game_icon = None;
            self.visible.clear();
            self.selected_id = None;
            self.dirty = false;
            self.status = "No trophy folder is open. Drag one into this window or click Open Folder.".into();
        }
        cx.notify();
    }

    fn rebuild(&mut self) {
        let Some(session) = self.session.as_ref() else { return };
        self.visible = (0..session.set.trophies.len())
            .filter(|&ix| {
                let trophy = &session.set.trophies[ix];
                match self.filter {
                    Filter::All => true,
                    Filter::Obtained => trophy.obtained,
                    Filter::Locked => !trophy.obtained,
                }
            }).collect();

        self.visible.sort_by(|&a, &b| {
            let first = &session.set.trophies[a];
            let second = &session.set.trophies[b];
            let ordering = match self.sort {
                SortKey::Id => first.id.cmp(&second.id),
                SortKey::Name => first.name.to_lowercase().cmp(&second.name.to_lowercase()),
                SortKey::Description => first.description.to_lowercase().cmp(&second.description.to_lowercase()),
                SortKey::Grade => grade_rank(first.grade).cmp(&grade_rank(second.grade)),
                SortKey::Hidden => first.hidden.cmp(&second.hidden),
                SortKey::Obtained => first.obtained.cmp(&second.obtained),
                SortKey::Synced => first.synced.cmp(&second.synced),
                SortKey::EarnedTime => first.earned_us.cmp(&second.earned_us),
                SortKey::Group => first.group_id.cmp(&second.group_id),
            };
            let ordering = if self.descending { ordering.reverse() } else { ordering };
            ordering.then_with(|| first.id.cmp(&second.id))
        });
    }

    fn set_sort(&mut self, key: SortKey, cx: &mut Context<Self>) {
        if self.sort == key {
            self.descending = !self.descending;
        } else {
            self.sort = key;
            self.descending = false;
        }
        self.rebuild();
        cx.notify();
    }

    fn toggle_tool_panel(&mut self, panel: ToolPanel, cx: &mut Context<Self>) {
        self.tool_panel = if self.tool_panel == panel { ToolPanel::None } else { panel };
        cx.notify();
    }

    fn set_filter(&mut self, filter: Filter, cx: &mut Context<Self>) {
        self.filter = filter;
        self.rebuild();
        cx.notify();
    }

    fn unlock_selected(&mut self, cx: &mut Context<Self>) {
        let Some(id) = self.selected_id else {
            self.status = "Select a trophy first.".into();
            cx.notify();
            return;
        };
        if let Some(session) = self.session.as_mut() {
            let at = chrono::Utc::now().timestamp_micros();
            match session.unlock_at_utc(id, at) {
                Ok(()) => {
                    self.status = format!("Changed trophy #{id} in memory; click Save to persist.");
                    self.dirty = true;
                    self.rebuild();
                }
                Err(e) => self.status = format!("Cannot unlock trophy #{id}: {e:#}"),
            }
        }
        cx.notify();
    }

    fn lock_selected(&mut self, cx: &mut Context<Self>) {
        let Some(id) = self.selected_id else {
            self.status = "Select a trophy first.".into();
            cx.notify();
            return;
        };
        if let Some(session) = self.session.as_mut() {
            match session.lock(id) {
                Ok(()) => {
                    self.status = format!("Locked trophy #{id} in memory; click Save to persist.");
                    self.dirty = true;
                    self.rebuild();
                }
                Err(e) => self.status = format!("Cannot lock trophy #{id}: {e:#}"),
            }
        }
        cx.notify();
    }

    fn select_trophy(&mut self, id: u32, cx: &mut Context<Self>) {
        self.selected_id = Some(id);
        let timestamp = self.session.as_ref()
            .and_then(|s| s.set.trophies.iter().find(|t| t.id == id))
            .and_then(|t| t.earned_us);
        let date = timestamp
            .and_then(chrono::DateTime::<chrono::Utc>::from_timestamp_micros)
            .unwrap_or_else(chrono::Utc::now)
            .to_rfc3339_opts(chrono::SecondsFormat::Secs, true);
        self.time_input.update(cx, |input, cx| input.emplace(&date, cx));
        cx.notify();
    }

    fn apply_selected_time(&mut self, cx: &mut Context<Self>) {
        let Some(id) = self.selected_id else {
            self.status = "Select a trophy to edit its obtainment time.".into();
            cx.notify();
            return;
        };
        let typed = self.time_input.read(cx).as_str().trim().to_owned();
        let parsed = chrono::DateTime::parse_from_rfc3339(&typed)
            .map(|time| time.timestamp_micros());
        match (self.session.as_mut(), parsed) {
            (_, Err(_)) => {
                self.status = "Invalid timestamp. Enter ISO-8601 with a timezone, e.g. 2026-11-16T12:00:00Z.".into();
            }
            (Some(session), Ok(micros)) => match session.unlock_at_utc(id, micros) {
                Ok(()) => {
                    self.dirty = true;
                    self.status = format!("Trophy #{id} timestamp updated in memory; click Save.");
                    self.rebuild();
                }
                Err(error) => self.status = format!("Cannot change trophy #{id}: {error:#}"),
            },
            (None, Ok(_)) => self.status = "Open a trophy folder first.".into(),
        }
        cx.notify();
    }

    fn instant_platinum(&mut self, cx: &mut Context<Self>) {
        if let Some(session) = self.session.as_mut() {
            // The original default random interval starts on 2008-01-01 and
            // ends at the current time; native PSN sync times tighten the
            // interval in the data layer.
            let parse = |s: &str| chrono::DateTime::parse_from_rfc3339(s.trim())
                .map(|date| date.timestamp_micros());
            let start = parse(self.random_start_input.read(cx).as_str());
            let end = parse(self.random_end_input.read(cx).as_str());
            let (Ok(start), Ok(end)) = (start, end) else {
                self.status = "Invalid random time interval: enter ISO-8601 dates with timezone.".into();
                cx.notify();
                return;
            };
            match session.instant_platinum(start, end) {
                Ok(count) => {
                    self.status = format!("{count} trophies unlocked in memory. Click Save to persist.");
                    if count > 0 { self.dirty = true; }
                    self.rebuild();
                }
                Err(error) => self.status = format!("Instant Platinum failed: {error:#}"),
            }
        }
        cx.notify();
    }

    fn clear_unsynced(&mut self, cx: &mut Context<Self>) {
        if let Some(session) = self.session.as_mut() {
            match session.clear_unsynced_tail() {
                Ok(count) => {
                    self.status = format!("{count} unsynchronized trophies cleared in memory. Click Save to persist.");
                    if count > 0 { self.dirty = true; }
                    self.rebuild();
                }
                Err(error) => self.status = format!("Clear trophies failed: {error:#}"),
            }
        }
        cx.notify();
    }

    fn toggle_smart_copy(&mut self, cx: &mut Context<Self>) {
        self.smart_copy_enabled = !self.smart_copy_enabled;
        cx.notify();
    }

    fn import_copy_from(&mut self, source: CopySource, cx: &mut Context<Self>) {
        let Some(session) = self.session.as_ref() else {
            self.status = "Open a PS3 trophy folder before importing Copy From.".into();
            cx.notify();
            return;
        };
        if self.is_copying {
            self.status = "An existing Copy From request is already running.".into();
            cx.notify();
            return;
        }
        let url = self.copy_url_input.read(cx).as_str().trim().to_owned();
        if url.is_empty() {
            self.status = "Enter a PSN Trophy Leaders / PSNProfiles HTTPS URL or local HTML path.".into();
            cx.notify();
            return;
        }
        let options = if self.smart_copy_enabled {
            let text = self.smart_options_input.read(cx).as_str().to_owned();
            let values: Vec<&str> = text.split(',').map(str::trim).collect();
            if values.len() != 5 {
                self.status = "Smart Copy expects years,months,days,minSeconds,maxSeconds.".into();
                cx.notify();
                return;
            }
            let parse = || -> anyhow::Result<SmartCopyOptions> {
                Ok(SmartCopyOptions {
                    years: values[0].parse()?,
                    months: values[1].parse()?,
                    days: values[2].parse()?,
                    min_jitter_seconds: values[3].parse()?,
                    max_jitter_seconds: values[4].parse()?,
                })
            };
            match parse() {
                Ok(opts) => Some(opts),
                Err(error) => {
                    self.status = format!("Invalid Smart Copy time offset: {error}");
                    cx.notify();
                    return;
                }
            }
        } else { None };
        let folder = session.source.clone();
        let local = session.set.trophies.clone();
        self.is_copying = true;
        self.status = format!("Fetching {} trophy identities...", source.label());
        cx.notify();

        // HTTP and DOM parsing run off the UI thread; only the validated
        // mapping comes back to the GPUI view. Nothing is saved automatically.
        cx.spawn(async move |this, cx| {
            let result = cx.background_spawn(async move {
                let html = load_copy_page(source, &url)?;
                let remote = parse_copy_page(source, &html)?;
                let count = remote.len();
                let mut mapping = map_remote_to_local(&local, &remote)?;
                if let Some(opts) = options {
                    smart_copy(&mut mapping, opts)?;
                }
                Ok::<_, anyhow::Error>((count, mapping))
            }).await;
            this.update(cx, |view, cx| {
                view.is_copying = false;
                if view.session.as_ref().is_none_or(|s| s.source != folder) {
                    view.status = "Copy From abandoned: a different trophy folder was opened.".into();
                    cx.notify();
                    return;
                }
                match result {
                    Err(error) => {
                        view.status = format!("Copy From failed without modifying trophies: {error:#}");
                    }
                    Ok((remote_count, mapping)) => {
                        let matched = mapping.len();
                        if let Some(session) = view.session.as_mut() {
                            match session.apply_copy_from(&mapping) {
                                Ok(changed) => {
                                    view.dirty |= changed > 0;
                                    view.status = format!(
                                        "{} source rows; {} identities matched; {} timestamps edited in memory. Click Save.",
                                        remote_count, matched, changed);
                                    view.rebuild();
                                }
                                Err(error) => view.status = format!(
                                    "Copy From edits rejected without modifying trophies: {error:#}"),
                            }
                        }
                    }
                }
                cx.notify();
            }).ok();
        }).detach();
    }

    fn select_resign_profile(&mut self, cx: &mut Context<Self>) {
        let Some(session) = self.session.as_mut() else {
            self.status = "Open a native PlayStation 3 trophy folder first.".into();
            cx.notify();
            return;
        };
        let Some(path) = rfd::FileDialog::new()
            .set_title("Select account profile SFO for PS3 re-signing")
            .add_filter("PlayStation 3 PARAM.SFO", &["sfo", "SFO"])
            .pick_file()
        else {
            return;
        };
        match session.select_profile_for_resign(&path) {
            Ok(()) => {
                self.dirty = true;
                self.status = format!("PS3 profile selected: {}. Click Save to re-sign.", path.display());
            }
            Err(error) => {
                self.status = format!("Cannot re-sign with that profile: {error:#}");
            }
        }
        cx.notify();
    }

    fn save(&mut self, cx: &mut Context<Self>) {
        let result = self.session.as_ref()
            .map(|session| (session.source.clone(), session.save_with_backup()));
        match result {
            Some((path, Ok(backup))) => {
                self.dirty = false;
                self.open_folder(path);
                self.status = format!("Saved trophy data. Originals backed up at {}", backup.display());
            }
            Some((_, Err(e))) => self.status = format!("Save refused: {e:#}"),
            None => self.status = "Open a trophy folder first.".into(),
        }
        cx.notify();
    }
}

fn grade_rank(grade: ps3trophyisgood::Grade) -> u8 {
    use ps3trophyisgood::Grade::*;
    match grade {
        Platinum => 0,
        Gold => 1,
        Silver => 2,
        Bronze => 3,
    }
}

fn toolbar_button(label: &'static str, active: bool) -> gpui::Stateful<gpui::Div> {
    div().id(label)
        .px_3().py_1p5()
        .rounded_md()
        .border_1().border_color(rgb(if active { 0x5b8ebf } else { 0x344358 }))
        .bg(rgb(if active { 0x273f58 } else { SURFACE }))
        .text_sm()
        .text_color(rgb(if active { 0xe0efff } else { TEXT }))
        .cursor_pointer()
        .child(label)
}

impl Render for TrophyApp {
    fn render(&mut self, _window: &mut Window, cx: &mut Context<Self>) -> impl IntoElement {
        let loaded = self.session.is_some();
        let title = self.session.as_ref()
            .map(|s| s.set.title.clone())
            .unwrap_or_else(|| "PS3TrophyIsGood".to_string());
        let count = self.visible.len();
        let total = self.session.as_ref().map_or(0, |s| s.set.trophies.len());
        let obtained = self.session.as_ref().map_or(0, |s| s.set.obtained_count());
        let synced_count = self.session.as_ref().map_or(0, |s| s.set.synced_count());
        let (earned_xp, total_xp) = self.session.as_ref()
            .map(|s| s.set.psn_xp()).unwrap_or((0, 0));
        let xp_percent = if total_xp == 0 { 0.0 } else {
            earned_xp as f32 / total_xp as f32
        };
        let format_label = self.session.as_ref().map(|s| s.set.format.label())
            .unwrap_or("PlayStation 3");
        let selected = self.selected_id.and_then(|id|
            self.session.as_ref()
                .and_then(|s| s.set.trophies.iter().find(|t| t.id == id)))
            .cloned();
        let open_button = div().id("open-folder")
            .px_4().py_2()
            .rounded_md().bg(rgb(ACCENT))
            .text_color(rgb(BG))
            .cursor_pointer()
            .child("Open Folder")
            .on_click(cx.listener(|this, _event, _window, cx| {
                if let Some(path) = rfd::FileDialog::new()
                    .set_title("Select PS3 trophy folder")
                    .pick_folder()
                {
                    this.open_folder(path);
                    cx.notify();
                }
            }));

        let sort_id = self.sort == SortKey::Id;
        let sort_name = self.sort == SortKey::Name;
        let sort_description = self.sort == SortKey::Description;
        let sort_grade = self.sort == SortKey::Grade;
        let sort_hidden = self.sort == SortKey::Hidden;
        let sort_obtained = self.sort == SortKey::Obtained;
        let sort_synced = self.sort == SortKey::Synced;
        let sort_date = self.sort == SortKey::EarnedTime;
        let sort_group = self.sort == SortKey::Group;
        let header = div()
            .w_full()
            .flex()
            .items_center()
            .gap_2()
            .h(px(38.))
            .px_3()
            .bg(rgb(SURFACE))
            .text_sm()
            .text_color(rgb(MUTED))
            .child(div().w(px(48.)).child(""))

            .child(div().id("sort-id").w(px(45.)).cursor_pointer().text_color(rgb(if sort_id { ACCENT } else { MUTED }))
                .child("ID").on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::Id, cx))))
            .child(div().id("sort-name").w(px(190.)).cursor_pointer().text_color(rgb(if sort_name { ACCENT } else { MUTED }))
                .child("Trophy Name")
                .on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::Name, cx))))

            .child(div().id("sort-description").w(px(280.)).cursor_pointer().text_color(rgb(if sort_description { ACCENT } else { MUTED }))
                .child("Description").on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::Description, cx))))
            .child(div().id("sort-grade").w(px(78.)).cursor_pointer().text_color(rgb(if sort_grade { ACCENT } else { MUTED }))
                .child("Type")
                .on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::Grade, cx))))

            .child(div().id("sort-hidden").w(px(58.)).cursor_pointer().text_color(rgb(if sort_hidden { ACCENT } else { MUTED }))
                .child("Hidden").on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::Hidden, cx))))

            .child(div().id("sort-obtained").w(px(55.)).cursor_pointer().text_color(rgb(if sort_obtained { ACCENT } else { MUTED }))
                .child("Get").on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::Obtained, cx))))

            .child(div().id("sort-synced").w(px(62.)).cursor_pointer().text_color(rgb(if sort_synced { ACCENT } else { MUTED }))
                .child("IsSync").on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::Synced, cx))))
            .child(div().id("sort-date").w(px(190.)).cursor_pointer().text_color(rgb(if sort_date { ACCENT } else { MUTED }))
                .child("Obtainment Time")
                .on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::EarnedTime, cx))))

            .child(div().id("sort-group").w(px(72.)).cursor_pointer().text_color(rgb(if sort_group { ACCENT } else { MUTED }))
                .child("From").on_click(cx.listener(|this, _, _, cx| this.set_sort(SortKey::Group, cx))));

        div()
            .id("trophy-app-root")
            .on_drop(cx.listener(|this, paths: &gpui::ExternalPaths, _window, cx| {
                if let Some(path) = paths.paths().iter().find(|p| {
                    p.is_dir() || p.file_name().is_some_and(|n|
                        n.to_string_lossy().eq_ignore_ascii_case("TROPCONF.SFM"))
                }) {
                    let folder = if path.is_dir() { path.clone() } else {
                        path.parent().unwrap_or(path).to_path_buf()
                    };
                    this.open_folder(folder);
                    cx.notify();
                }
            }))
            .size_full()
            .flex()
            .flex_col()
            .bg(rgb(BG))
            .text_color(rgb(TEXT))
            // Compact native application command bar. Actions do not compete
            // with the trophy table for vertical space.
            .child(
                div().w_full().px_5().py_3().bg(rgb(SURFACE))
                    .border_b_1().border_color(rgb(0x2e3a4b))
                    .flex().items_center().justify_between().gap_4()
                    .child(div().min_w_0().flex().items_center().gap_3()
                        .child(div().w(px(46.)).h(px(46.))
                            .rounded_md().bg(rgb(0x29384a))
                            .when_some(self.game_icon.clone(), |d, image|
                                d.child(gpui::img(image).w(px(46.)).h(px(46.)))))
                        .child(div().min_w_0().flex().flex_col().gap_1()
                            .child(div().text_lg().font_weight(gpui::FontWeight::SEMIBOLD)
                                .overflow_hidden().child(title))
                            .child(div().text_xs().text_color(rgb(MUTED))
                                .child(format!("{format_label}  ·  {}",
                                    self.session.as_ref().map(|s| s.set.npcommid.as_str())
                                        .unwrap_or("Drop a trophy folder to begin"))))))
                    .child(div().flex().items_center().gap_2()
                        .child(open_button)
                        .child(toolbar_button(if self.dirty { "Save *" } else { "Save" }, self.dirty)
                            .on_click(cx.listener(|this, _, _, cx| this.save(cx))))
                        .child(toolbar_button("Refresh", false)
                            .on_click(cx.listener(|this, _, _, cx| this.refresh_folder(cx))))
                        .child(toolbar_button("Close", false)
                            .on_click(cx.listener(|this, _, _, cx| this.close_folder(cx)))))
            )
            // Keep the original PSN EXP distinct from merely obtained trophies.
            .child(
                div().w_full().px_5().py_2().bg(rgb(0x141b25))
                    .border_b_1().border_color(rgb(0x2e3a4b))
                    .flex().items_center().justify_between().gap_4()
                    .child(div().flex().items_center().gap_5()
                        .child(div().text_sm().text_color(rgb(TEXT))
                            .child(format!("Obtained  {obtained} / {total}")))
                        .child(div().text_sm().text_color(rgb(MUTED))
                            .child(format!("PSN synced  {synced_count} / {total}"))))
                    .child(div().flex().items_center().gap_3()
                        .child(div().text_sm().text_color(rgb(ACCENT))
                            .child(format!("PSN EXP   {earned_xp} / {total_xp}")))
                        .child(div().w(px(180.)).h(px(7.)).rounded_full()
                            .bg(rgb(0x2c394a))
                            .child(div().h_full().rounded_full().bg(rgb(ACCENT))
                                .w(px(180. * xp_percent))))
                        .child(div().text_xs().text_color(rgb(MUTED))
                            .child(format!("{:.1}%", xp_percent * 100.0))))
            )
            .child(
                div().w_full().px_4().py_2().bg(rgb(BG))
                    .flex().items_center().justify_between().gap_3()
                    .child(div().flex().items_center().gap_2()
                        .child(div().text_sm().font_weight(gpui::FontWeight::SEMIBOLD)
                            .child("Trophies"))
                        .child(toolbar_button("All", self.filter == Filter::All)
                            .on_click(cx.listener(|this, _, _, cx| this.set_filter(Filter::All, cx))))
                        .child(toolbar_button("Obtained", self.filter == Filter::Obtained)
                            .on_click(cx.listener(|this, _, _, cx| this.set_filter(Filter::Obtained, cx))))
                        .child(toolbar_button("Locked", self.filter == Filter::Locked)
                            .on_click(cx.listener(|this, _, _, cx| this.set_filter(Filter::Locked, cx))))
                        .child(div().px_2().text_xs().text_color(rgb(MUTED))
                            .child(format!("{count} shown  ·  Double-click to unlock / lock"))))
                    .child(div().flex().items_center().gap_2()
                        .child(toolbar_button("Advanced", self.tool_panel == ToolPanel::Batch)
                            .on_click(cx.listener(|this, _, _, cx|
                                this.toggle_tool_panel(ToolPanel::Batch, cx))))
                        .child(toolbar_button("Copy From", self.tool_panel == ToolPanel::CopyFrom)
                            .on_click(cx.listener(|this, _, _, cx|
                                this.toggle_tool_panel(ToolPanel::CopyFrom, cx)))))
            )
            .child(
                div()
                    .flex_1()
                    .min_h_0()
                    .flex()
                    .flex_col()
                    .px_4()
                    .child(
                        div().flex_1().min_h_0().overflow_scroll()
                            .border_1().border_color(rgb(0x30394a))
                            .child(
                                div().min_w(px(1200.)).w_full().h_full()
                                    .flex().flex_col()
                                    .child(header)
                                    .child(
                                uniform_list("trophy-rows", count, cx.processor(|this, range: std::ops::Range<usize>, _window, cx| {
                                    let mut elements = Vec::with_capacity(range.len());
                                    for index in range {
                                        let Some(&ix) = this.visible.get(index) else { continue };
                                        let Some(trophy) = this.session.as_ref()
                                            .and_then(|s| s.set.trophies.get(ix)).cloned() else { continue };
                                        let id = trophy.id;
                                        let obtained = trophy.obtained;
                                        let selected = this.selected_id == Some(id);
                                        let icon = this.icons.get(&id).cloned();
                                        let row = div().id(("trophy", id))
                                            .h(px(50.)).w_full()
                                            .px_3().flex().items_center().gap_2()
                                            .bg(rgb(if selected { 0x29466a } else if index % 2 == 0 { BG } else { 0x151923 }))
                                            .border_b_1().border_color(rgb(0x242a34))
                                            .text_sm()
                                            .cursor_pointer()
                                            .child(div().w(px(48.)).h(px(42.))
                                                .when_some(icon, |d, image|
                                                    d.child(gpui::img(image).w(px(42.)).h(px(42.)))))
                                            .child(div().w(px(45.)).text_color(rgb(MUTED)).child(format!("#{id:03}")))
                                            .child(div().w(px(190.)).overflow_hidden().child(trophy.name))
                                            .child(div().w(px(280.)).overflow_hidden().text_color(rgb(MUTED)).child(trophy.description.clone()))
                                            .child(div().w(px(78.)).text_color(rgb(match trophy.grade { ps3trophyisgood::Grade::Platinum => 0xf4cce2, ps3trophyisgood::Grade::Gold => 0xf9d98a, ps3trophyisgood::Grade::Silver => 0xd1daec, ps3trophyisgood::Grade::Bronze => 0xddac7e })).child(trophy.grade.label()))
                                            .child(div().w(px(58.)).child(if trophy.hidden { "Yes" } else { "No" }))
                                            .child(div().w(px(55.)).child(if trophy.obtained { "Yes" } else { "No" }))
                                            .child(div().w(px(62.)).child(if trophy.synced { "Yes" } else { "No" }))
                                            .child(div().w(px(190.)).text_color(rgb(if trophy.obtained { TEXT } else { MUTED }))
                                                .child(format_timestamp(trophy.earned_us)))
                                            .child(div().w(px(72.)).text_color(rgb(MUTED))
                                                .child(if trophy.group_id == 0 { "Base".into() } else { format!("DLC {}", trophy.group_id) }))
                                            .on_click(cx.listener(move |this, event: &gpui::ClickEvent, _, cx| {
                                                 this.select_trophy(id, cx);
                                                 if event.click_count() >= 2 {
                                                     if obtained {
                                                         this.lock_selected(cx);
                                                     } else {
                                                         this.unlock_selected(cx);
                                                     }
                                                 }
                                             }));
                                        elements.push(row);
                                    }
                                    elements
                                }))
                                .h_full()
                                    )
                            )
                    )
            )
            .when_some(selected, |root, trophy| {
                root.child(
                    div()
                        .px_5().py_3()
                        .w_full()
                        .bg(rgb(HOVER))
                        .border_t_1().border_color(rgb(0x39475b))
                        .flex().flex_col().gap_1()
                        .child(div().text_sm().child(format!("#{}  {}  ·  {}",
                            trophy.id, trophy.name, trophy.grade.label())))
                        .child(div().text_sm().text_color(rgb(MUTED)).child(trophy.description))
                        .child(div().flex().items_center().gap_2().pt_2()
                            .child(div().text_sm().text_color(rgb(MUTED)).child("Obtainment time"))
                            .child(text_input("obtainment-time")
                                .state(self.time_input.downgrade())
                                .placeholder("2026-11-16T12:00:00Z")
                                .w(px(260.)).h(px(36.)).px_2()
                                .rounded_md().border_1().border_color(rgb(0x455268))
                                .bg(rgb(BG)).text_color(rgb(TEXT)))
                            .child(toolbar_button("Apply Time", false)
                                .on_click(cx.listener(|this, _, _, cx| this.apply_selected_time(cx))))
                            .child(toolbar_button("Unlock Now", false)
                                .on_click(cx.listener(|this, _, _, cx| this.unlock_selected(cx))))
                            .child(toolbar_button("Lock Selected", false)
                                .on_click(cx.listener(|this, _, _, cx| this.lock_selected(cx)))))
                )
            })
            .when(self.tool_panel == ToolPanel::Batch, |root| root.child(
                div().w_full().px_5().py_3().bg(rgb(SURFACE))
                    .border_t_1().border_color(rgb(0x30394a))
                    .flex().items_center().gap_2()
                    .text_sm()
                    .child(div().text_color(rgb(MUTED)).child("Random start"))
                    .child(text_input("random-start")
                        .state(self.random_start_input.downgrade())
                        .placeholder("2008-01-01T00:00:00Z")
                        .w(px(215.)).h(px(34.)).px_2().rounded_md()
                        .border_1().border_color(rgb(0x455268))
                        .bg(rgb(BG)).text_color(rgb(TEXT)))
                    .child(div().text_color(rgb(MUTED)).child("End"))
                    .child(text_input("random-end")
                        .state(self.random_end_input.downgrade())
                        .placeholder("2026-11-16T12:00:00Z")
                        .w(px(215.)).h(px(34.)).px_2().rounded_md()
                        .border_1().border_color(rgb(0x455268))
                        .bg(rgb(BG)).text_color(rgb(TEXT)))
                    .child(toolbar_button("Instant Platinum", false)
                        .on_click(cx.listener(|this, _, _, cx| this.instant_platinum(cx))))
                    .child(toolbar_button("Clear Unsynced", false)
                        .on_click(cx.listener(|this, _, _, cx| this.clear_unsynced(cx))))
                    .child(toolbar_button("Resign Profile", false)
                        .on_click(cx.listener(|this, _, _, cx| this.select_resign_profile(cx))))
            ))
            .when(self.tool_panel == ToolPanel::CopyFrom, |root| root.child(
                div().w_full().px_5().py_3().bg(rgb(BG))
                    .border_t_1().border_color(rgb(0x30394a))
                    .flex().items_center().gap_2().text_sm()
                    .child(div().text_color(rgb(MUTED)).child("Copy From"))
                    .child(text_input("copy-url")
                        .state(self.copy_url_input.downgrade())
                        .placeholder("HTTPS PSN trophy URL or local HTML file path")
                        .w(px(395.)).h(px(34.)).px_2().rounded_md()
                        .border_1().border_color(rgb(0x455268))
                        .bg(rgb(SURFACE)).text_color(rgb(TEXT)))
                    .child(toolbar_button("PSNProfiles", false)
                        .on_click(cx.listener(|this, _, _, cx|
                            this.import_copy_from(CopySource::PsnProfiles, cx))))
                    .child(toolbar_button("PSN Leaders", false)
                        .on_click(cx.listener(|this, _, _, cx|
                            this.import_copy_from(CopySource::PsnTrophyLeaders, cx))))
                    .child(toolbar_button(if self.smart_copy_enabled {
                            "Smart Copy ON"
                        } else {
                            "Smart Copy OFF"
                        }, self.smart_copy_enabled)
                        .on_click(cx.listener(|this, _, _, cx| this.toggle_smart_copy(cx))))
                    .child(text_input("smart-copy-options")
                        .state(self.smart_options_input.downgrade())
                        .placeholder("years,months,days,minSec,maxSec")
                        .w(px(210.)).h(px(34.)).px_2().rounded_md()
                        .border_1().border_color(rgb(0x455268))
                        .bg(rgb(SURFACE)).text_color(rgb(TEXT)))
            ))
            .child(
                div()
                    .w_full().px_5().py_2()
                    .bg(rgb(SURFACE))
                    .text_sm().text_color(rgb(MUTED))
                    .child(if loaded {
                        self.status.clone()
                    } else {
                        self.status.clone()
                    })
            )
    }
}

pub fn run() {
    application().run(|cx: &mut App| {
        cx.bind_keys(default_bindings().as_keybindings(Some(DEFAULT_INPUT_CONTEXT)));
        let bounds = Bounds::centered(None, size(px(1320.), px(780.)), cx);
        if let Err(error) = cx.open_window(
            WindowOptions {
                window_bounds: Some(WindowBounds::Windowed(bounds)),
                ..Default::default()
            },
            |window, cx| {
                let view = cx.new(|cx| TrophyApp::new(cx));
                let weak = view.downgrade();
                window.on_window_should_close(cx, move |_window, app| {
                    weak.update(app, |view, cx| {
                        let confirmed = view.confirm_unsaved();
                        cx.notify();
                        confirmed
                    }).unwrap_or(true)
                });
                view
            },
        ) {
            eprintln!("Could not create GPU window: {error}");
        }
        cx.activate(true);
    });
}
