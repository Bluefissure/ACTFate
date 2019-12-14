import json
import argparse
import codecs, csv
def get_config():
    parser = argparse.ArgumentParser(description='fates Auto-update Script')

    parser.add_argument('-i', '--input', type=str, required=True,
                        help='Input json file to be updated.')
    parser.add_argument('-l', '--language', type=str, required=True, choices=['en', 'ja', 'fr', 'de', 'ko', 'zh'],
                        help='The language to be updated.')
    parser.add_argument('-c', '--csv', type=str, required=True,
                        help='Exported Fate.csv used for updating. ')
    # Parse args.
    args = parser.parse_args()
    # Namespace => Dictionary.
    kwargs = vars(args)
    return kwargs

if __name__=="__main__":
    config = get_config()
    input_file = config.get("input")
    with codecs.open(input_file, "r", "utf8") as f:
        input_json = json.load(f)
    fates = input_json.get("fates")
    lang = config.get("language")
    lang_list = ['en', 'ja', 'fr', 'ko', 'zh']
    name_idx = {
        'en':26,
        'ja':26,
        'fr':26,
        'de':26,
        'ko':26,
        'zh':26
    }
    with codecs.open(config.get("csv"), "r", "utf8") as f:
        spamreader = csv.reader(f)
        start = False
        for row in spamreader:
            if row[0] == "0":
                start = True
            if not start:
                continue
            (key, name) = (row[0], row[name_idx[lang]])
            if name:
                if key in fates:
                    if lang not in fates[key]["name"] or fates[key]["name"][lang]!=name:
                        print("Updating fate {}:{}".format(lang, name))
                    fates[key]["name"].update({
                        lang: name
                    })
                else:
                    fates[key] = {
                        "name": {},
                        "area_code": {}
                    }
                    print("Adding fate {}:{}".format(lang, name))
                    for tmp_lang in lang_list:
                        fates[key]["name"].update({
                            tmp_lang: name
                        })
                    for tmp_lang in lang_list:
                        fates[key]["area_code"].update({
                            tmp_lang: "0"
                        })

    fates = dict(sorted(fates.items(), key=lambda x:int(x[0])))
    
    output_file = "fates"
    if f"_{lang}" not in config.get("input"):
        output_file += f"_{lang}"
    output_file += ".json"
    with codecs.open(output_file, "w", "utf8") as f:
        json.dump({"fates":fates}, f, ensure_ascii=False, sort_keys=False, indent=2)
