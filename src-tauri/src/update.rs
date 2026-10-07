use serde::Serialize;
use serde_json::Value;

// Newest release first, pre-releases included (portable builds ship as pre-releases).
const RELEASES: &str = "https://api.github.com/repos/SquamosS/Game-Tracker/releases?per_page=1";

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct UpdateInfo {
    pub version: String,
    pub url: String,
}

fn parse(v: &str) -> Vec<u64> {
    v.trim_start_matches('v').split('.').map(|p| p.parse().unwrap_or(0)).collect()
}

/// The latest GitHub Release, if it is newer than `current`.
pub async fn check(client: &reqwest::Client, current: &str) -> Option<UpdateInfo> {
    let releases: Value = client
        .get(RELEASES)
        .header("User-Agent", "game-tracker")
        .send()
        .await
        .ok()?
        .json()
        .await
        .ok()?;
    let release = releases.get(0)?;
    let tag = release["tag_name"].as_str()?;
    (parse(tag) > parse(current)).then(|| UpdateInfo {
        version: tag.trim_start_matches('v').to_string(),
        url: release["html_url"].as_str().unwrap_or_default().to_string(),
    })
}

#[cfg(test)]
mod tests {
    use super::parse;

    #[test]
    fn compares_versions_numerically() {
        assert!(parse("v0.10.0") > parse("0.9.1"));
        assert!(parse("v0.2.0") > parse("0.1.0"));
        assert!(parse("v0.1.0") <= parse("0.1.0"));
    }
}
