import json
import argparse
import codecs, csv
def get_config():
    parser = argparse.ArgumentParser(description='Instances Auto-update Script')

    parser.add_argument('-i', '--input', type=str, required=True,
                        help='Input json file to be updated.')
    parser.add_argument('-l', '--language', type=str, required=True, choices=['en', 'ja', 'fr', 'de', 'ko', 'zh'],
                        help='The language to be updated.')
    parser.add_argument('-c', '--csv', type=str, required=True,
                        help='Exported ContentFinderCondition.csv used for updating. ')
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
    instances = input_json.get("instances")
    lang = config.get("language")
    lang_list = ['en', 'ja', 'fr', 'de', 'ko', 'zh']
    name_idx = {
        'en':35,
        'ja':35,
        'fr':35,
        'de':35,
        'ko':35,
        'zh':35
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
                if key in instances:
                    if lang not in instances[key]["name"] or instances[key]["name"][lang]!=name:
                        print("Updating instance {}:{}".format(lang, name))
                    instances[key]["name"].update({
                        lang: name
                    })
                else:
                    instances[key] = {
                        "name": {},
                        "t": "0",
                        "h": "0",
                        "d": "0",
                    }
                    print("Adding instance {}:{}".format(lang, name))
                    for tmp_lang in lang_list:
                        instances[key]["name"].update({
                            tmp_lang: name
                        })

    instances = dict(sorted(instances.items(), key=lambda x:int(x[0])))
    
    output_file = "instances"
    if f"_{lang}" not in config.get("input"):
        output_file += f"_{lang}"
    output_file += ".json"
    with codecs.open(output_file, "w", "utf8") as f:
        json.dump({"instances":instances}, f, ensure_ascii=False, sort_keys=False, indent=2)
