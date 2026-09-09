//! herddesk-filebridge serve --stdio --protocol 1.0

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if args.len() == 4
        && args[0] == "serve"
        && args[1] == "--stdio"
        && args[2] == "--protocol"
        && args[3] == "1.0"
    {
        std::process::exit(herddesk_filebridge::server::serve_stdio());
    }
    eprintln!("filebridge_usage");
    std::process::exit(2);
}
